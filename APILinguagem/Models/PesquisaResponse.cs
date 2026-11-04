namespace APILinguagem.Models;

public sealed record PesquisaResponse<T>(IReadOnlyList<T> Resultados, int SaldoPontos);
