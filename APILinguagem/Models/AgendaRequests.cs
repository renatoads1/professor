using System.ComponentModel.DataAnnotations;

namespace APILinguagem.Models;

public sealed record AtualizarDisponibilidadeSemanalRequest(
    [Required] string FusoHorarioId,
    [Required] IReadOnlyList<FaixaDisponibilidadeRequest> Faixas);

public sealed record DisponibilidadeSemanalResponse(
    string? FusoHorarioId,
    IReadOnlyList<FaixaDisponibilidadeRequest> Faixas);

public sealed record FaixaDisponibilidadeRequest(
    DayOfWeek DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFim);

public sealed record ReservarAulaRequest(
    int ProfessorId,
    DateTimeOffset InicioUtc,
    Guid ChaveIdempotencia);

public sealed record HorarioDisponivelResponse(
    DateTimeOffset InicioUtc,
    DateTimeOffset FimUtc,
    int QuantidadePontos);

public sealed record HorarioAgendaResponse(
    DateTimeOffset InicioUtc,
    DateTimeOffset FimUtc,
    int QuantidadePontos,
    bool Disponivel);

public sealed record AulaAgendadaResponse(
    long Id,
    int ProfessorId,
    DateTimeOffset InicioUtc,
    DateTimeOffset FimUtc,
    int QuantidadePontos,
    string Status);
