namespace APILinguagem.Models;

public sealed record PesquisaProfessoresRequest
{
    public Guid ChaveIdempotencia { get; init; }
    public string? Id { get; init; }
    public string? Nome { get; init; }
    public string? Email { get; init; }
    public string? Telefone { get; init; }
    public string? Pais { get; init; }
    public string? Estado { get; init; }
    public string? Cidade { get; init; }
    public string? LinguaDeEnsino { get; init; }
    public string? Apresentacao { get; init; }
    public decimal? PrecoMinimo { get; init; }
    public decimal? PrecoMaximo { get; init; }
    public bool? Ativo { get; init; }
}
