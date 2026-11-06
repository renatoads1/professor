using System.ComponentModel.DataAnnotations;

namespace APILinguagem.Models;

public sealed record PacotePontosRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Nome { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int QuantidadePontos { get; init; }

    [Range(100, 100_000_000, ErrorMessage = "O preço deve estar entre R$ 1,00 e R$ 1.000.000,00.")]
    public int PrecoCentavos { get; init; }
}
