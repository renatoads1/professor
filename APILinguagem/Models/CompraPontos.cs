namespace APILinguagem.Models;

public sealed class CompraPontos
{
    private CompraPontos()
    {
    }

    public CompraPontos(
        int contaAcessoId,
        int quantidadePontos,
        int precoCentavos,
        string identificadorProvedor,
        string status,
        DateTimeOffset expiraEm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contaAcessoId);
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
    public int QuantidadePontos { get; private set; }
    public int PrecoCentavos { get; private set; }
    public string IdentificadorProvedor { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset ExpiraEm { get; private set; }
}
