namespace APILinguagem.Models;

public sealed class CompraPontos
{
    private CompraPontos()
    {
    }

    public CompraPontos(
        int contaAcessoId,
        int pacotePontosId,
        int quantidadePontos,
        int precoCentavos,
        string identificadorProvedor,
        string status,
        DateTimeOffset expiraEm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contaAcessoId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pacotePontosId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantidadePontos);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(precoCentavos);

        if (string.IsNullOrWhiteSpace(identificadorProvedor) || identificadorProvedor.Trim().Length > 200)
        {
            throw new ArgumentException("O identificador do provedor deve ter entre 1 e 200 caracteres.", nameof(identificadorProvedor));
        }

        if (string.IsNullOrWhiteSpace(status) || status.Trim().Length > 30)
        {
            throw new ArgumentException("O status deve ter entre 1 e 30 caracteres.", nameof(status));
        }

        if (expiraEm <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expiraEm), "A expiração deve ser futura.");
        }

        ContaAcessoId = contaAcessoId;
        PacotePontosId = pacotePontosId;
        QuantidadePontos = quantidadePontos;
        PrecoCentavos = precoCentavos;
        IdentificadorProvedor = identificadorProvedor.Trim();
        Status = status.Trim();
        CriadaEm = DateTimeOffset.UtcNow;
        ExpiraEm = expiraEm.ToUniversalTime();
    }

    public long Id { get; private set; }
    public int ContaAcessoId { get; private set; }
    public ContaAcesso ContaAcesso { get; private set; } = null!;
    public int? PacotePontosId { get; private set; }
    public PacotePontos? PacotePontos { get; private set; }
    public int QuantidadePontos { get; private set; }
    public int PrecoCentavos { get; private set; }
    public string IdentificadorProvedor { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string? IdentificadorCobranca { get; private set; }
    public string? PixCopiaECola { get; private set; }
    public string? QrCodeBase64 { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset ExpiraEm { get; private set; }
    public DateTimeOffset? DataPagamento { get; private set; }

    public void DefinirDadosPix(string identificadorCobranca, string pixCopiaECola, string qrCodeBase64)
    {
        if (Status != CompraPontosStatus.Pendente)
        {
            throw new InvalidOperationException("Somente compras pendentes podem receber dados Pix.");
        }

        if (string.IsNullOrWhiteSpace(identificadorCobranca) || identificadorCobranca.Trim().Length > 200)
        {
            throw new ArgumentException("O identificador da cobrança é inválido.", nameof(identificadorCobranca));
        }

        if (string.IsNullOrWhiteSpace(pixCopiaECola) || string.IsNullOrWhiteSpace(qrCodeBase64))
        {
            throw new ArgumentException("A cobrança deve conter o código Pix e o QR Code.");
        }

        IdentificadorCobranca = identificadorCobranca.Trim();
        PixCopiaECola = pixCopiaECola;
        QrCodeBase64 = qrCodeBase64;
    }

    public void MarcarComoPaga(DateTimeOffset dataPagamento)
    {
        if (Status == CompraPontosStatus.Paga)
        {
            return;
        }

        if (Status is not (CompraPontosStatus.Pendente or CompraPontosStatus.Expirada))
        {
            throw new InvalidOperationException("Somente compras pendentes ou expiradas podem ser pagas após confirmação do provedor.");
        }

        Status = CompraPontosStatus.Paga;
        DataPagamento = dataPagamento.ToUniversalTime();
    }

    public void MarcarComoExpirada()
    {
        if (Status == CompraPontosStatus.Pendente)
        {
            Status = CompraPontosStatus.Expirada;
        }
    }
}

public static class CompraPontosStatus
{
    public const string Pendente = "Pendente";
    public const string Paga = "Paga";
    public const string Expirada = "Expirada";
    public const string Cancelada = "Cancelada";
}
