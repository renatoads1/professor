using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QRCoder;

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
    public async Task<InterPixCriacaoResult> CriarCobrancaAsync(
        string txid,
        int precoCentavos,
        int expiracaoSegundos,
        string descricao,
        CancellationToken cancellationToken)
    {
        options.Value.Validate(environment.ContentRootPath);
        var uri = CriarUri($"pix/v2/cob/{Uri.EscapeDataString(txid)}");
        var valorOriginal = (precoCentavos / 100m).ToString("F2", CultureInfo.InvariantCulture);
        var request = new JsonObject
        {
            ["calendario"] = new JsonObject { ["expiracao"] = expiracaoSegundos },
            ["valor"] = new JsonObject { ["original"] = valorOriginal },
            ["chave"] = options.Value.PixKey,
            ["solicitacaoPagador"] = descricao
        };

        using var response = await EnviarAutenticadoAsync(HttpMethod.Put, uri, request.ToJsonString(), cancellationToken);
        await GarantirSucessoAsync(response, cancellationToken);
        var cobranca = await LerCobrancaAsync(response, cancellationToken);
        if (!string.Equals(cobranca.Txid, txid, StringComparison.Ordinal))
        {
            throw new InvalidDataException("O txid retornado pelo Inter não corresponde à cobrança solicitada.");
        }

        if (cobranca.Loc?.Id is not long localizacaoId)
        {
            throw new InvalidDataException("O Inter não retornou a localização da cobrança Pix.");
        }

        if (string.IsNullOrWhiteSpace(cobranca.PixCopiaECola))
        {
            throw new InvalidDataException("O Inter não retornou o código Pix copia e cola.");
        }

        using var geradorQrCode = new QRCodeGenerator();
        using var dadosQrCode = geradorQrCode.CreateQrCode(cobranca.PixCopiaECola, QRCodeGenerator.ECCLevel.M);
        using var qrCodePng = new PngByteQRCode(dadosQrCode);
        var imagemBase64 = Convert.ToBase64String(qrCodePng.GetGraphic(10));

        return new InterPixCriacaoResult(
            localizacaoId.ToString(CultureInfo.InvariantCulture),
            cobranca.PixCopiaECola,
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
        await GarantirSucessoAsync(response, cancellationToken);

        var cobranca = await LerCobrancaAsync(response, cancellationToken);

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
        var corpo = new JsonObject { ["webhookUrl"] = options.Value.WebhookUrl! };
        using var response = await EnviarAutenticadoAsync(
            HttpMethod.Put,
            CriarUri($"pix/v2/webhook/{Uri.EscapeDataString(options.Value.PixKey)}"),
            corpo.ToJsonString(),
            cancellationToken);
        await GarantirSucessoAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> EnviarAutenticadoAsync(
        HttpMethod method,
        Uri uri,
        string? corpoJson,
        CancellationToken cancellationToken)
    {
        var accessToken = await tokenProvider.ObterTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (corpoJson is not null)
        {
            request.Content = new StringContent(corpoJson, Encoding.UTF8, "application/json");
        }

        return await httpClientFactory.CreateClient("InterPix").SendAsync(request, cancellationToken);
    }

    private Uri CriarUri(string path) => new(
        $"{options.Value.BaseUrl.TrimEnd('/')}/{path}",
        UriKind.Absolute);

    private static async Task GarantirSucessoAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = response.StatusCode;
        var corpo = await response.Content.ReadAsStringAsync(cancellationToken);
        var detalhe = corpo.Length > 500 ? corpo[..500] : corpo;
        throw new HttpRequestException(
            $"A API Pix do Inter respondeu com HTTP {(int)statusCode} ({statusCode}). Resposta: {detalhe}",
            null,
            statusCode);
    }

    private static async Task<CobrancaResponse> LerCobrancaAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = ParseJsonResposta(json, "O Inter retornou uma cobrança Pix vazia.");
        var root = document.RootElement;

        var valor = root.TryGetProperty("valor", out var valorElement) && valorElement.ValueKind == JsonValueKind.Object
            ? new ValorResponse(TextoOuNulo(valorElement, "original"))
            : null;

        var loc = root.TryGetProperty("loc", out var locElement) && locElement.ValueKind == JsonValueKind.Object
            ? new LocResponse(
                locElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id) ? id : (long?)null,
                TextoOuNulo(locElement, "location"))
            : null;

        var pix = new List<PixResponse>();
        if (root.TryGetProperty("pix", out var pixElement) && pixElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pixElement.EnumerateArray())
            {
                pix.Add(new PixResponse(TextoOuNulo(item, "txid"), TextoOuNulo(item, "valor"), TextoOuNulo(item, "horario")));
            }
        }

        return new CobrancaResponse
        {
            Txid = TextoOuNulo(root, "txid"),
            Status = TextoOuNulo(root, "status"),
            PixCopiaECola = TextoOuNulo(root, "pixCopiaECola"),
            Valor = valor,
            Loc = loc,
            Pix = pix
        };
    }

    private static JsonDocument ParseJsonResposta(string json, string mensagemVazia)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException(mensagemVazia);
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("O Inter retornou uma resposta que não é JSON válido.", exception);
        }
    }

    private static string? TextoOuNulo(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object
        && elemento.TryGetProperty(propriedade, out var valor)
        && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    private sealed record CobrancaResponse
    {
        public string? Txid { get; init; }
        public string? Status { get; init; }
        public string? PixCopiaECola { get; init; }
        public ValorResponse? Valor { get; init; }
        public LocResponse? Loc { get; init; }
        public IReadOnlyList<PixResponse>? Pix { get; init; }
    }

    private sealed record ValorResponse(string? Original);
    private sealed record LocResponse(long? Id, string? Location);
    private sealed record PixResponse(string? Txid, string? Valor, string? Horario);
}

public sealed class InterAccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<InterAccessTokenProvider> logger,
    IOptions<InterPixOptions> options,
    IHostEnvironment environment)
{
    private static readonly TimeSpan CooldownFalhaOAuth = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiraEm;
    private DateTimeOffset _novaTentativaOAuthEm;
    private string? _mensagemFalhaOAuth;
    private HttpStatusCode? _statusFalhaOAuth;

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

            if (_novaTentativaOAuthEm > DateTimeOffset.UtcNow)
            {
                throw new HttpRequestException(_mensagemFalhaOAuth, null, _statusFalhaOAuth);
            }

            HttpResponseMessage response;
            try
            {
                response = await httpClientFactory.CreateClient("InterOAuth").PostAsync(
                    $"{options.Value.BaseUrl.TrimEnd('/')}/oauth/v2/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = options.Value.ClientId,
                        ["client_secret"] = options.Value.ClientSecret,
                        ["grant_type"] = "client_credentials",
                        ["scope"] = options.Value.Scope
                    }),
                    cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(
                    exception,
                    "Falha ao conectar ao endpoint OAuth do Inter em {BaseUrl}. Tipo={HttpRequestError}; Status={StatusCode}; ExcecaoInterna={InnerException}",
                    options.Value.BaseUrl,
                    exception.HttpRequestError,
                    exception.StatusCode,
                    exception.InnerException?.ToString());
                AplicarCooldownOAuth(exception.Message, exception.StatusCode);
                throw;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var mensagem = $"A autenticação OAuth do Inter respondeu com HTTP {(int)response.StatusCode} ({response.StatusCode}).";
                    AplicarCooldownOAuth(mensagem, response.StatusCode);
                    throw new HttpRequestException(mensagem, null, response.StatusCode);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                {
                    throw new InvalidDataException("O Inter retornou uma resposta OAuth vazia.");
                }

                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("O Inter retornou uma resposta OAuth vazia.");
                }

                var accessToken = root.TryGetProperty("access_token", out var accessTokenElement)
                    ? accessTokenElement.GetString()
                    : null;
                var expiresIn = root.TryGetProperty("expires_in", out var expiresInElement)
                    && expiresInElement.TryGetInt32(out var segundos)
                        ? segundos
                        : 0;
                if (string.IsNullOrWhiteSpace(accessToken) || expiresIn <= 0)
                {
                    throw new InvalidDataException("A resposta OAuth do Inter não contém um token válido.");
                }

                _accessToken = accessToken;
                _expiraEm = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expiresIn - 60));
                return _accessToken;
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private void AplicarCooldownOAuth(string mensagem, HttpStatusCode? status)
    {
        _novaTentativaOAuthEm = DateTimeOffset.UtcNow.Add(CooldownFalhaOAuth);
        _mensagemFalhaOAuth = mensagem;
        _statusFalhaOAuth = status;
    }
}

public sealed record InterPixCriacaoResult(string IdentificadorCobranca, string PixCopiaECola, string? QrCodeBase64);
public sealed record InterPixCobranca(string Txid, string Status, string? ValorOriginal, IReadOnlyList<InterPixPagamento> Pagamentos);
public sealed record InterPixPagamento(string Txid, string Valor, DateTimeOffset? Horario = null);
