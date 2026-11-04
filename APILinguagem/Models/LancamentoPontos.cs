namespace APILinguagem.Models;

public sealed class LancamentoPontos
{
    private LancamentoPontos()
    {
    }

    public LancamentoPontos(int contaAcessoId, int quantidade, string motivo, string chaveIdempotencia)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contaAcessoId);

        if (quantidade == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantidade), "A quantidade de pontos não pode ser zero.");
        }

        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 200)
        {
            throw new ArgumentException("O motivo deve ter entre 1 e 200 caracteres.", nameof(motivo));
        }

        if (string.IsNullOrWhiteSpace(chaveIdempotencia) || chaveIdempotencia.Trim().Length > 100)
        {
            throw new ArgumentException("A chave de idempotência deve ter entre 1 e 100 caracteres.", nameof(chaveIdempotencia));
        }

        ContaAcessoId = contaAcessoId;
        Quantidade = quantidade;
        Motivo = motivo.Trim();
        ChaveIdempotencia = chaveIdempotencia.Trim();
        DataLancamento = DateTimeOffset.UtcNow;
    }

    public long Id { get; private set; }
    public int ContaAcessoId { get; private set; }
    public ContaAcesso ContaAcesso { get; private set; } = null!;
    public int Quantidade { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public DateTimeOffset DataLancamento { get; private set; }
    public string ChaveIdempotencia { get; private set; } = string.Empty;
}
