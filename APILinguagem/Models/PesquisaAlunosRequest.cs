namespace APILinguagem.Models;

public sealed record PesquisaAlunosRequest
{
    public Guid ChaveIdempotencia { get; init; }
    public string? Id { get; init; }
    public string? Nome { get; init; }
    public string? Email { get; init; }
    public string? Telefone { get; init; }
    public string? Pais { get; init; }
    public string? Estado { get; init; }
    public string? Cidade { get; init; }
    public string? LinguaDeInteresse { get; init; }
    public bool? Estudando { get; init; }
}
