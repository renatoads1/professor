namespace APILinguagem.Models;

public sealed class PacotePontos
{
    private PacotePontos()
    {
    }

    public PacotePontos(string nome, int quantidadePontos, int precoCentavos)
    {
        DefinirNome(nome);
        DefinirValores(quantidadePontos, precoCentavos);
        Ativo = true;
        CriadoEm = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public int QuantidadePontos { get; private set; }
    public int PrecoCentavos { get; private set; }
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public void Atualizar(string nome, int quantidadePontos, int precoCentavos)
    {
        DefinirNome(nome);
        DefinirValores(quantidadePontos, precoCentavos);
    }

    public void Desativar() => Ativo = false;

    public void Ativar() => Ativo = true;

    private void DefinirNome(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome) || nome.Trim().Length > 100)
        {
            throw new ArgumentException("O nome do pacote deve ter entre 1 e 100 caracteres.", nameof(nome));
        }

        Nome = nome.Trim();
    }

    private void DefinirValores(int quantidadePontos, int precoCentavos)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantidadePontos);
        if (precoCentavos is < 100 or > 100_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(precoCentavos), "O preço deve estar entre R$ 1,00 e R$ 1.000.000,00.");
        }
        QuantidadePontos = quantidadePontos;
        PrecoCentavos = precoCentavos;
    }
}
