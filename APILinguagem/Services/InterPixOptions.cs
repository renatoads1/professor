using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace APILinguagem.Services;

public sealed class InterPixOptions
{
    public const string SectionName = "InterPix";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://cdpj-sandbox.partners.uatinter.co";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string PixKey { get; set; } = string.Empty;
    public string CertificatePath { get; set; } = string.Empty;
    public string? PrivateKeyPath { get; set; }
    public string? CertificatePassword { get; set; }
    public string Scope { get; set; } = "cob.write cob.read pix.read webhook.write";
    public string? WebhookUrl { get; set; }
    public int ExpirationMinutes { get; set; } = 30;
    public int ReconciliationIntervalSeconds { get; set; } = 60;

    public bool IsConfigured(string contentRootPath) => Enabled
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(PixKey)
        && !string.IsNullOrWhiteSpace(CertificatePath)
        && File.Exists(ResolvePath(CertificatePath, contentRootPath))
        && (string.IsNullOrWhiteSpace(PrivateKeyPath)
            || File.Exists(ResolvePath(PrivateKeyPath, contentRootPath)));

    public void Validate(string contentRootPath, bool requireWebhook = false)
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("A integração Pix do Inter está desabilitada na configuração da API.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("InterPix:BaseUrl deve ser uma URL HTTPS válida.");
        }

        if (string.IsNullOrWhiteSpace(ClientId)
            || string.IsNullOrWhiteSpace(ClientSecret)
            || string.IsNullOrWhiteSpace(PixKey)
            || string.IsNullOrWhiteSpace(CertificatePath))
        {
            throw new InvalidOperationException("Configure ClientId, ClientSecret, PixKey e CertificatePath em InterPix na API.");
        }

        var certificatePath = ResolvePath(CertificatePath, contentRootPath);
        if (!File.Exists(certificatePath))
        {
            throw new InvalidOperationException("O certificado configurado em InterPix:CertificatePath não foi encontrado.");
        }

        if (!string.IsNullOrWhiteSpace(PrivateKeyPath)
            && !File.Exists(ResolvePath(PrivateKeyPath, contentRootPath)))
        {
            throw new InvalidOperationException("A chave privada configurada em InterPix:PrivateKeyPath não foi encontrada.");
        }

        if (ExpirationMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("InterPix:ExpirationMinutes deve estar entre 1 e 1440.");
        }

        if (requireWebhook
            && (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var webhookUri)
                || webhookUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Configure InterPix:WebhookUrl com uma URL HTTPS pública da API.");
        }

        try
        {
            using var certificate = LoadCertificate(contentRootPath);
            if (certificate is null || !certificate.HasPrivateKey)
            {
                throw new InvalidOperationException("O certificado InterPix deve conter ou estar associado à chave privada.");
            }
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("Não foi possível carregar o certificado Pix configurado na API.", exception);
        }
    }

    public X509Certificate2? LoadCertificate(string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(CertificatePath))
        {
            return null;
        }

        var certificatePath = ResolvePath(CertificatePath, contentRootPath);
        if (!File.Exists(certificatePath))
        {
            return null;
        }

        if (Path.GetExtension(certificatePath).Equals(".pfx", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(certificatePath).Equals(".p12", StringComparison.OrdinalIgnoreCase))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                CertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
        }

        var privateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath)
            ? certificatePath
            : ResolvePath(PrivateKeyPath, contentRootPath);
        return X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath);
    }

    private static string ResolvePath(string path, string contentRootPath) => Path.IsPathRooted(path)
        ? path
        : Path.GetFullPath(Path.Combine(contentRootPath, path));
}

public static class InterPixHttpMessageHandlerFactory
{
    public static HttpMessageHandler Create(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<InterPixOptions>>().Value;
        var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
        var handler = new HttpClientHandler();
        var certificate = options.LoadCertificate(environment.ContentRootPath);
        if (certificate is not null)
        {
            handler.ClientCertificates.Add(certificate);
        }

        return handler;
    }
}
