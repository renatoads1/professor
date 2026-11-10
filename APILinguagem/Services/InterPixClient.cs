using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace APILinguagem.Services;

public interface IInterPixClient
{
    Task<InterPixCriacaoResult> CriarCobrancaAsync(
        string txid,
        int precoCentavos,
        int expiracaoSegundos,
        string descricao,
        CancellationToken cancellationToken);

    Task<InterPixCobranca> ConsultarCobrancaAsync(string txid, CancellationToken cancellationToken);
    Task ConfigurarWebhookAsync(CancellationToken cancellationToken);
}

public sealed class InterPixClient(
    IHttpClientFactory httpClientFactory,
    InterAccessTokenProvider tokenProvider,
    IOptions<InterPixOptions> options,
    IHostEnvironment environment) : IInterPixClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InterPixCriacaoResult> CriarCobrancaAsync(
        string txid,
        int precoCentavos,
        int expiracaoSegundos,
        string descricao,
        CancellationToken cancellationToken)
    {
        options.Value.Validate(environment.ContentRootPath);
        var uri = CriarUri($"pix/v2/cob/{Uri.EscapeDataString(txid)}");
        var request = new CriarCobrancaRequest(
            new CalendarioRequest(expiracaoSegundos),
            new ValorRequest((precoCentavos / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
            options.Value.PixKey,
            descricao);

        using var response = await EnviarAutenticadoAsync(HttpMethod.Put, uri, request, cancellationToken);
        await GarantirSucessoAsync(response);
        var cobranca = await response.Content.ReadFromJsonAsync<CobrancaResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("O Inter retornou uma cobrança Pix vazia.");
        if (!string.Equals(cobranca.Txid, txid, StringComparison.Ordinal))
        {
            throw new InvalidDataException("O txid retornado pelo Inter não corresponde à cobrança solicitada.");
        }

        if (cobranca.Loc?.Id is not long localizacaoId)
        {
            throw new InvalidDataException("O Inter não retornou a localização do QR Code Pix.");
        }

        using var qrResponse = await EnviarAutenticadoAsync(
            HttpMethod.Get,
            CriarUri($"pix/v2/loc/{localizacaoId}/qrcode"),
            null,
            cancellationToken);
        await GarantirSucessoAsync(qrResponse);
        var qrCode = await qrResponse.Content.ReadFromJsonAsync<QrCodeResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("O Inter retornou um QR Code Pix vazio.");
        if (string.IsNullOrWhiteSpace(qrCode.Qrcode) || string.IsNullOrWhiteSpace(qrCode.ImagemQrcode))
        {
            throw new InvalidDataException("O Inter não retornou o código Pix e a imagem do QR Code.");
        }

        var imagemBase64 = qrCode.ImagemQrcode;
        var prefixoBase64 = imagemBase64.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (prefixoBase64 >= 0)
        {
            imagemBase64 = imagemBase64[(prefixoBase64 + "base64,".Length)..];
        }

        try
        {
            _ = Convert.FromBase64String(imagemBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("O Inter retornou uma imagem QR Code que não está em Base64 válido.", exception);
        }

        return new InterPixCriacaoResult(
            localizacaoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            qrCode.Qrcode,
            imagemBase64);
    }

    public async Task<InterPixCobranca> ConsultarCobrancaAsync(string txid, CancellationToken cancellationToken)
    {
        options.Value.Validate(environment.ContentRootPath);
        using var response = await EnviarAutenticadoAsync(
            HttpMethod.Get,
            CriarUri($"pix/v2/cob/{Uri.EscapeDataString(txid)}"),
            null,
            cancellationToken);
        await GarantirSucessoAsync(response);

        var cobranca = await response.Content.ReadFromJsonAsync<CobrancaResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("O Inter retornou uma cobrança Pix vazia.");

        return new InterPixCobranca(
            cobranca.Txid ?? string.Empty,
            cobranca.Status ?? string.Empty,
            cobranca.Valor?.Original,
            (cobranca.Pix ?? []).Select(pix => new InterPixPagamento(
                pix.Txid ?? string.Empty,
                pix.Valor ?? string.Empty,
                DateTimeOffset.TryParse(pix.Horario, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var horario)
                    ? horario
                    : null)).ToArray());
    }

    public async Task ConfigurarWebhookAsync(CancellationToken cancellationToken)
    {
        options.Value.Validate(environment.ContentRootPath, requireWebhook: true);
        var url = options.Value.WebhookUrl!;
        using var response = await EnviarAutenticadoAsync(
            HttpMethod.Put,
            CriarUri($"pix/v2/webhook/{Uri.EscapeDataString(options.Value.PixKey)}"),
            new ConfigurarWebhookRequest(url),
            cancellationToken);
        await GarantirSucessoAsync(response);
    }

    private async Task<HttpResponseMessage> EnviarAutenticadoAsync(
        HttpMethod method,
        Uri uri,
        object? body,
        CancellationToken cancellationToken)
    {
        var accessToken = await tokenProvider.ObterTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await httpClientFactory.CreateClient("InterPix").SendAsync(request, cancellationToken);
    }

    private Uri CriarUri(string path) => new(
        $"{options.Value.BaseUrl.TrimEnd('/')}/{path}",
        UriKind.Absolute);

    private static Task GarantirSucessoAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        var statusCode = response.StatusCode;
        throw new HttpRequestException(
            $"A API Pix do Inter respondeu com HTTP {(int)statusCode} ({statusCode}).",
            null,
            statusCode);
    }

    private sealed record CriarCobrancaRequest(
        CalendarioRequest Calendario,
        ValorRequest Valor,
        string Chave,
        string SolicitacaoPagador);

    private sealed record CalendarioRequest(int Expiracao);
    private sealed record ValorRequest(string Original);
    private sealed record ConfigurarWebhookRequest(string WebhookUrl);

    private sealed record CobrancaResponse
    {
        public string? Txid { get; init; }
        public string? Status { get; init; }
        public ValorResponse? Valor { get; init; }
        public LocResponse? Loc { get; init; }
        public IReadOnlyList<PixResponse>? Pix { get; init; }
    }

    private sealed record ValorResponse(string? Original);
    private sealed record LocResponse(long? Id, string? Location);
    private sealed record PixResponse(string? Txid, string? Valor, string? Horario);
    private sealed record QrCodeResponse(string? Qrcode, string? ImagemQrcode);
}

public sealed class InterAccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<InterPixOptions> options,
    IHostEnvironment environment)
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiraEm;

    public async Task<string> ObterTokenAsync(CancellationToken cancellationToken)
    {
        options.Value.Validate(environment.ContentRootPath);
        if (_accessToken is not null && _expiraEm > DateTimeOffset.UtcNow)
        {
            return _accessToken;
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _expiraEm > DateTimeOffset.UtcNow)
            {
                return _accessToken;
            }

            using var response = await httpClientFactory.CreateClient("InterOAuth").PostAsync(
                $"{options.Value.BaseUrl.TrimEnd('/')}/oauth/v2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.Value.ClientId,
                    ["client_secret"] = options.Value.ClientSecret,
                    ["grant_type"] = "client_credentials",
                    ["scope"] = options.Value.Scope
                }),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"A autenticação OAuth do Inter respondeu com HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    null,
                    response.StatusCode);
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("O Inter retornou uma resposta OAuth vazia.");
            if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn <= 0)
            {
                throw new InvalidDataException("A resposta OAuth do Inter não contém um token válido.");
            }

            _accessToken = token.AccessToken;
            _expiraEm = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, token.ExpiresIn - 60));
            return _accessToken;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private sealed record TokenResponse(string? AccessToken, int ExpiresIn);
}

public sealed record InterPixCriacaoResult(string IdentificadorCobranca, string PixCopiaECola, string QrCodeBase64);
public sealed record InterPixCobranca(string Txid, string Status, string? ValorOriginal, IReadOnlyList<InterPixPagamento> Pagamentos);
public sealed record InterPixPagamento(string Txid, string Valor, DateTimeOffset? Horario = null);
