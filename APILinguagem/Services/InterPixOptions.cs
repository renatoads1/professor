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
            var certificate = LoadCertificate(contentRootPath);
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

    public X509Certificate2? LoadCertificate(string contentRootPath) =>
        InterPixCertificateLoader.Load(this, contentRootPath);

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
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual
        };
        var certificate = options.LoadCertificate(environment.ContentRootPath);
        if (certificate is not null)
        {
            handler.ClientCertificates.Add(certificate);
        }

        return handler;
    }
}

file static class InterPixCertificateLoader
{
    private static readonly object Gate = new();
    private static string? _cacheKey;
    private static X509Certificate2? _certificate;
    private static readonly X509KeyStorageFlags[] WindowsKeyStorageFlags =
    [
        // IIS Express não abre a chave do perfil do usuário no handshake, mesmo quando a importação não lança erro.
        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet,
        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet
    ];

    public static X509Certificate2? Load(InterPixOptions options, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            return null;
        }

        var certificatePath = ResolvePath(options.CertificatePath, contentRootPath);
        if (!File.Exists(certificatePath))
        {
            return null;
        }

        var privateKeyPath = string.IsNullOrWhiteSpace(options.PrivateKeyPath)
            ? null
            : ResolvePath(options.PrivateKeyPath, contentRootPath);
        var cacheKey = string.Join(
            '|',
            certificatePath,
            privateKeyPath,
            File.GetLastWriteTimeUtc(certificatePath).Ticks,
            privateKeyPath is not null && File.Exists(privateKeyPath) ? File.GetLastWriteTimeUtc(privateKeyPath).Ticks : 0,
            options.CertificatePassword);

        lock (Gate)
        {
            if (_cacheKey == cacheKey)
            {
                return _certificate;
            }

            var loaded = LoadUncached(options, certificatePath, privateKeyPath);
            _certificate = loaded;
            _cacheKey = cacheKey;
            return _certificate;
        }
    }

    private static X509Certificate2 LoadUncached(InterPixOptions options, string certificatePath, string? privateKeyPath)
    {
        if (Path.GetExtension(certificatePath).Equals(".pfx", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(certificatePath).Equals(".p12", StringComparison.OrdinalIgnoreCase))
        {
            return ImportPkcs12(File.ReadAllBytes(certificatePath), options.CertificatePassword);
        }

        var keyPath = privateKeyPath ?? certificatePath;
        // CreateFromPemFile deixa a chave efêmera. O Schannel do Windows recusa essa chave
        // com "As credenciais fornecidas para o pacote não foram reconhecidas".
        using var pemCertificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
        return ImportPkcs12(pemCertificate.Export(X509ContentType.Pkcs12), password: null);
    }

    private static X509Certificate2 ImportPkcs12(byte[] pkcs12, string? password)
    {
        if (!OperatingSystem.IsWindows())
        {
            return X509CertificateLoader.LoadPkcs12(pkcs12, password, X509KeyStorageFlags.EphemeralKeySet);
        }

        CryptographicException? lastError = null;
        foreach (var flags in WindowsKeyStorageFlags)
        {
            try
            {
                return X509CertificateLoader.LoadPkcs12(pkcs12, password, flags);
            }
            catch (CryptographicException exception)
            {
                lastError = exception;
            }
        }

        throw lastError ?? new CryptographicException("Não foi possível importar a chave privada do certificado InterPix.");
    }

    private static string ResolvePath(string path, string contentRootPath) => Path.IsPathRooted(path)
        ? path
        : Path.GetFullPath(Path.Combine(contentRootPath, path));
}
