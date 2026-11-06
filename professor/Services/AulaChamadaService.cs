using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace professor.Services;

public sealed class AulaChamadaService(
    HttpClient httpClient,
    AuthTokenService authTokenService,
    IJSRuntime jsRuntime,
    IConfiguration configuration,
    ILogger<AulaChamadaService> logger) : IAsyncDisposable
{
    private const string ModuloWebRtcPath = "./aulaWebRtc.js";
    private HubConnection? _hub;
    private IJSObjectReference? _moduloWebRtc;
    private DotNetObjectReference<AulaChamadaService>? _referenciaDotNet;
    private long? _aulaId;
    private bool _encerrando;

    public event Func<Task>? EstadoAlterado;
    public string Estado { get; private set; } = "Pronto para entrar na aula.";
    public bool MicrofoneAtivo { get; private set; } = true;
    public bool CameraAtiva { get; private set; } = true;
    public bool Professor { get; private set; }
    public bool EmChamada => _hub?.State == HubConnectionState.Connected && _aulaId.HasValue;

    public async Task EntrarAsync(long aulaId, ElementReference videoLocal, ElementReference videoRemoto)
    {
        if (aulaId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(aulaId));
        }

        if (!authTokenService.IsAuthenticated || string.IsNullOrWhiteSpace(authTokenService.AccessToken))
        {
            throw new InvalidOperationException("É necessário entrar na plataforma antes de iniciar a chamada.");
        }

        if (_hub is not null)
        {
            throw new InvalidOperationException("Esta chamada já foi iniciada.");
        }

        _encerrando = false;
        _aulaId = aulaId;
        await DefinirEstadoAsync("Solicitando acesso à câmera e ao microfone...");
        try
        {
            _moduloWebRtc = await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModuloWebRtcPath);
            _referenciaDotNet = DotNetObjectReference.Create(this);
            await _moduloWebRtc.InvokeVoidAsync("iniciar", videoLocal, videoRemoto, _referenciaDotNet, ObterServidoresIce());

            var hubUri = new Uri(httpClient.BaseAddress
                ?? throw new InvalidOperationException("A URL base da API não está configurada."), "hubs/aulas");
            _hub = new HubConnectionBuilder()
                .WithUrl(hubUri, options => options.AccessTokenProvider = () => Task.FromResult(authTokenService.AccessToken))
                .WithAutomaticReconnect()
                .Build();
            RegistrarEventosHub(_hub);
            RegistrarEventosConexao(_hub);

            await DefinirEstadoAsync("Conectando à sala da aula...");
            await _hub.StartAsync();
            var entrada = await _hub.InvokeAsync<AulaHubEntradaResponseCliente>("EntrarNaAulaAsync", aulaId);
            Professor = entrada.Professor;
            MicrofoneAtivo = true;
            CameraAtiva = true;
            await DefinirEstadoAsync("Na sala. Aguardando a entrada do outro participante...");
        }
        catch
        {
            _encerrando = true;
            await EncerrarRecursosAsync();
            _aulaId = null;
            _encerrando = false;
            throw;
        }
    }

    public async Task AlternarMicrofoneAsync()
    {
        if (_moduloWebRtc is null)
        {
            return;
        }

        MicrofoneAtivo = !MicrofoneAtivo;
        await _moduloWebRtc.InvokeVoidAsync("definirMicrofoneAtivo", MicrofoneAtivo);
        await NotificarEstadoAlteradoAsync();
    }

    public async Task AlternarCameraAsync()
    {
        if (_moduloWebRtc is null)
        {
            return;
        }

        CameraAtiva = !CameraAtiva;
        await _moduloWebRtc.InvokeVoidAsync("definirCameraAtiva", CameraAtiva);
        await NotificarEstadoAlteradoAsync();
    }

    public async Task SairAsync()
    {
        _encerrando = true;
        if (_hub is { State: HubConnectionState.Connected } && _aulaId is long aulaId)
        {
            try
            {
                await _hub.InvokeAsync("SairDaAulaAsync", aulaId);
            }
            catch (Exception exception) when (exception is HubException or HttpRequestException or OperationCanceledException)
            {
                logger.LogDebug(exception, "Não foi possível notificar a saída da aula {AulaId}.", aulaId);
            }
        }

        await EncerrarRecursosAsync();
        _aulaId = null;
        await DefinirEstadoAsync("Você saiu da aula.");
    }

    [JSInvokable]
    public async Task EnviarCandidatoIceAsync(CandidatoIceCliente candidato)
    {
        if (_hub is { State: HubConnectionState.Connected } && _aulaId is long aulaId)
        {
            await _hub.InvokeAsync("EnviarCandidatoAsync", aulaId, candidato);
        }
    }

    [JSInvokable]
    public Task AtualizarEstadoConexaoAsync(string estado) =>
        DefinirEstadoAsync(estado switch
        {
            "connected" => "Conexão de vídeo estabelecida.",
            "connecting" => "Estabelecendo conexão de vídeo...",
            "disconnected" => "A conexão de vídeo foi interrompida.",
            "failed" => "Não foi possível estabelecer a conexão de vídeo. Verifique a rede e o servidor TURN.",
            "closed" => "A conexão de vídeo foi encerrada.",
            _ => "Aguardando conexão de vídeo..."
        });

    public async ValueTask DisposeAsync()
    {
        await SairAsync();
        _referenciaDotNet?.Dispose();
        if (_moduloWebRtc is not null)
        {
            await _moduloWebRtc.DisposeAsync();
        }
    }

    private void RegistrarEventosHub(HubConnection hub)
    {
        hub.On("ParticipanteEntrou", CriarOfertaAsync);
        hub.On<string>("OfertaRecebida", ReceberOfertaAsync);
        hub.On<string>("RespostaRecebida", ReceberRespostaAsync);
        hub.On<CandidatoIceCliente>("CandidatoRecebido", ReceberCandidatoAsync);
        hub.On("ParticipanteSaiu", () => DefinirEstadoAsync("O outro participante saiu da chamada."));
    }

    private void RegistrarEventosConexao(HubConnection hub)
    {
        hub.Reconnecting += _ => DefinirEstadoAsync("Reconectando a chamada...");
        hub.Reconnected += async _ =>
        {
            if (_aulaId is long aulaId)
            {
                try
                {
                    await hub.InvokeAsync("EntrarNaAulaAsync", aulaId);
                    await DefinirEstadoAsync("Sinalização reconectada. Aguardando o participante...");
                }
                catch (Exception exception) when (exception is HubException or HttpRequestException)
                {
                    logger.LogWarning(exception, "Não foi possível retornar à sala da aula {AulaId}.", aulaId);
                    await DefinirEstadoAsync("Não foi possível retornar à sala da aula.");
                }
            }
        };
        hub.Closed += exception =>
        {
            if (exception is not null)
            {
                logger.LogWarning(exception, "A conexão SignalR da chamada foi encerrada.");
            }

            return _encerrando
                ? Task.CompletedTask
                : DefinirEstadoAsync("A conexão de sinalização foi encerrada.");
        };
    }

    private async Task CriarOfertaAsync()
    {
        if (_moduloWebRtc is null || _hub is not { State: HubConnectionState.Connected } || _aulaId is not long aulaId)
        {
            return;
        }

        var sdp = await _moduloWebRtc.InvokeAsync<string>("criarOferta");
        await _hub.InvokeAsync("EnviarOfertaAsync", aulaId, sdp);
        await DefinirEstadoAsync("Convite de vídeo enviado.");
    }

    private async Task ReceberOfertaAsync(string sdp)
    {
        if (_moduloWebRtc is null || _hub is not { State: HubConnectionState.Connected } || _aulaId is not long aulaId)
        {
            return;
        }

        var resposta = await _moduloWebRtc.InvokeAsync<string>("criarResposta", sdp);
        await _hub.InvokeAsync("EnviarRespostaAsync", aulaId, resposta);
    }

    private async Task ReceberRespostaAsync(string sdp)
    {
        if (_moduloWebRtc is not null)
        {
            await _moduloWebRtc.InvokeVoidAsync("definirResposta", sdp);
        }
    }

    private async Task ReceberCandidatoAsync(CandidatoIceCliente candidato)
    {
        if (_moduloWebRtc is not null)
        {
            await _moduloWebRtc.InvokeVoidAsync("adicionarCandidato", candidato);
        }
    }

    private async Task EncerrarRecursosAsync()
    {
        if (_moduloWebRtc is not null)
        {
            try
            {
                await _moduloWebRtc.InvokeVoidAsync("encerrar");
            }
            catch (JSDisconnectedException exception)
            {
                logger.LogDebug(exception, "O runtime do navegador já foi desconectado durante a limpeza da chamada.");
            }
        }

        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }

    private IceServerCliente[] ObterServidoresIce() =>
        configuration.GetSection("WebRtc:IceServers").Get<IceServerCliente[]>()
        ?? [new IceServerCliente { Urls = ["stun:stun.l.google.com:19302"] }];

    private async Task DefinirEstadoAsync(string estado)
    {
        Estado = estado;
        await NotificarEstadoAlteradoAsync();
    }

    private Task NotificarEstadoAlteradoAsync() => EstadoAlterado?.Invoke() ?? Task.CompletedTask;
}

public sealed class IceServerCliente
{
    public string[] Urls { get; set; } = [];
    public string? Username { get; set; }
    public string? Credential { get; set; }
}

public sealed record AulaHubEntradaResponseCliente(bool Professor);

public sealed record CandidatoIceCliente(string Candidato, string? SdpMid, int? SdpMLineIndex);
