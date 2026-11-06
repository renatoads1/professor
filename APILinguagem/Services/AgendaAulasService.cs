using System.Security.Cryptography;
using APILinguagem.Data;
using APILinguagem.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using professor.Model;

namespace APILinguagem.Services;

public sealed class AgendaAulasService(AlunosDbContext dbContext)
{
    private const string TipoAluno = "aluno";
    private const string TipoProfessor = "professor";
    private const int DuracaoAulaMinutos = 60;
    private const int MaximoDiasConsulta = 31;

    public async Task<AgendaOperacaoResult> DefinirDisponibilidadeAsync(
        int contaAcessoId,
        AtualizarDisponibilidadeSemanalRequest request,
        CancellationToken cancellationToken)
    {
        var validacao = ValidarFaixas(request);
        if (validacao != AgendaOperacaoStatus.Concluida)
        {
            return new(validacao);
        }

        if (!TryObterFusoHorario(request.FusoHorarioId, out _))
        {
            return new(AgendaOperacaoStatus.FusoHorarioInvalido);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var conta = await ObterContaBloqueadaAsync(contaAcessoId, cancellationToken);
        if (conta is null)
        {
            return new(AgendaOperacaoStatus.ContaNaoEncontrada);
        }

        if (conta.TipoPerfil != TipoProfessor)
        {
            return new(AgendaOperacaoStatus.PerfilIncorreto);
        }

        var professor = await dbContext.Professores
            .SingleOrDefaultAsync(item => item.Id == conta.PerfilId, cancellationToken);
        if (professor is null)
        {
            return new(AgendaOperacaoStatus.ProfessorNaoEncontrado);
        }

        if (!professor.Ativo)
        {
            return new(AgendaOperacaoStatus.ProfessorInativo);
        }

        await dbContext.DisponibilidadesSemanaisProfessores
            .Where(item => item.ProfessorId == professor.Id)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.DisponibilidadesSemanaisProfessores.AddRange(request.Faixas.Select(faixa =>
            new DisponibilidadeSemanalProfessor
            {
                ProfessorId = professor.Id,
                DiaSemana = faixa.DiaSemana,
                HoraInicio = faixa.HoraInicio,
                HoraFim = faixa.HoraFim,
                FusoHorarioId = request.FusoHorarioId.Trim()
            }));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(AgendaOperacaoStatus.Concluida);
    }

    public async Task<AgendaOperacaoResult<DisponibilidadeSemanalResponse>> ObterMinhaDisponibilidadeSemanalAsync(
        int contaAcessoId,
        CancellationToken cancellationToken)
    {
        var conta = await dbContext.ContasAcesso
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == contaAcessoId, cancellationToken);
        if (conta is null)
        {
            return new(AgendaOperacaoStatus.ContaNaoEncontrada);
        }

        if (conta.TipoPerfil != TipoProfessor)
        {
            return new(AgendaOperacaoStatus.PerfilIncorreto);
        }

        var professorExiste = await dbContext.Professores
            .AsNoTracking()
            .AnyAsync(item => item.Id == conta.PerfilId && item.Ativo, cancellationToken);
        if (!professorExiste)
        {
            return new(AgendaOperacaoStatus.ProfessorNaoEncontrado);
        }

        var faixas = await dbContext.DisponibilidadesSemanaisProfessores
            .AsNoTracking()
            .Where(item => item.ProfessorId == conta.PerfilId)
            .OrderBy(item => item.DiaSemana)
            .ThenBy(item => item.HoraInicio)
            .Select(item => new FaixaDisponibilidadeRequest(item.DiaSemana, item.HoraInicio, item.HoraFim))
            .ToArrayAsync(cancellationToken);
        var fusoHorarioId = await dbContext.DisponibilidadesSemanaisProfessores
            .AsNoTracking()
            .Where(item => item.ProfessorId == conta.PerfilId)
            .Select(item => item.FusoHorarioId)
            .FirstOrDefaultAsync(cancellationToken);

        return new(AgendaOperacaoStatus.Concluida, new(fusoHorarioId, faixas));
    }

    public async Task<AgendaOperacaoResult<IReadOnlyList<HorarioAgendaResponse>>> ListarMinhaAgendaAsync(
        int contaAcessoId,
        DateTimeOffset inicioUtc,
        DateTimeOffset fimUtc,
        CancellationToken cancellationToken)
    {
        var conta = await dbContext.ContasAcesso
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == contaAcessoId, cancellationToken);
        if (conta is null)
        {
            return new(AgendaOperacaoStatus.ContaNaoEncontrada, []);
        }

        if (conta.TipoPerfil != TipoProfessor)
        {
            return new(AgendaOperacaoStatus.PerfilIncorreto, []);
        }

        return await ListarAgendaProfessorAsync(conta.PerfilId, inicioUtc, fimUtc, cancellationToken);
    }

    public async Task<AgendaOperacaoStatus> LimparDisponibilidadeAsync(
        int contaAcessoId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var conta = await ObterContaBloqueadaAsync(contaAcessoId, cancellationToken);
        if (conta is null)
        {
            return AgendaOperacaoStatus.ContaNaoEncontrada;
        }

        if (conta.TipoPerfil != TipoProfessor)
        {
            return AgendaOperacaoStatus.PerfilIncorreto;
        }

        var professorExiste = await dbContext.Professores
            .AnyAsync(item => item.Id == conta.PerfilId && item.Ativo, cancellationToken);
        if (!professorExiste)
        {
            return AgendaOperacaoStatus.ProfessorNaoEncontrado;
        }

        await dbContext.DisponibilidadesSemanaisProfessores
            .Where(item => item.ProfessorId == conta.PerfilId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AgendaOperacaoStatus.Concluida;
    }

    public async Task<AgendaOperacaoResult<IReadOnlyList<HorarioDisponivelResponse>>> ListarHorariosDisponiveisAsync(
        int professorId,
        DateTimeOffset inicioUtc,
        DateTimeOffset fimUtc,
        CancellationToken cancellationToken)
    {
        var resultado = await ListarAgendaProfessorAsync(professorId, inicioUtc, fimUtc, cancellationToken);
        var horariosDisponiveis = resultado.Status == AgendaOperacaoStatus.Concluida
            ? resultado.Valor!.Where(horario => horario.Disponivel)
                .Select(horario => new HorarioDisponivelResponse(horario.InicioUtc, horario.FimUtc, horario.QuantidadePontos))
                .ToArray()
            : [];
        return new(resultado.Status, horariosDisponiveis);
    }

    public async Task<AgendaOperacaoResult<IReadOnlyList<HorarioAgendaResponse>>> ListarAgendaProfessorAsync(
        int professorId,
        DateTimeOffset inicioUtc,
        DateTimeOffset fimUtc,
        CancellationToken cancellationToken)
    {
        if (professorId <= 0 || fimUtc <= inicioUtc || (fimUtc - inicioUtc).TotalDays > MaximoDiasConsulta)
        {
            return new(AgendaOperacaoStatus.EntradaInvalida, []);
        }

        var professor = await dbContext.Professores
            .AsNoTracking()
            .Where(item => item.Id == professorId && item.Ativo)
            .Select(item => new { item.HoraAula })
            .SingleOrDefaultAsync(cancellationToken);
        if (professor is null)
        {
            return new(AgendaOperacaoStatus.ProfessorNaoEncontrado, []);
        }

        if (!TryObterQuantidadePontos(professor.HoraAula, out var quantidadePontos))
        {
            return new(AgendaOperacaoStatus.TarifaInvalida, []);
        }

        var faixas = await dbContext.DisponibilidadesSemanaisProfessores
            .AsNoTracking()
            .Where(item => item.ProfessorId == professorId)
            .ToListAsync(cancellationToken);
        if (faixas.Count == 0)
        {
            return new(AgendaOperacaoStatus.Concluida, []);
        }

        var fusos = faixas.Select(item => item.FusoHorarioId).Distinct(StringComparer.Ordinal).ToArray();
        if (fusos.Length != 1 || !TryObterFusoHorario(fusos[0], out var fusoHorario))
        {
            return new(AgendaOperacaoStatus.FusoHorarioInvalido, []);
        }

        var inicioConsultaUtc = inicioUtc.ToUniversalTime();
        var fimConsultaUtc = fimUtc.ToUniversalTime();
        var ocupadas = await dbContext.AulasAgendadas
            .AsNoTracking()
            .Where(aula => aula.ProfessorId == professorId
                && aula.Status != StatusAula.Concluida
                && aula.InicioUtc < fimConsultaUtc
                && aula.FimUtc > inicioConsultaUtc)
            .Select(aula => new { aula.InicioUtc, aula.FimUtc })
            .ToListAsync(cancellationToken);

        var primeiraData = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(inicioConsultaUtc, fusoHorario).DateTime);
        var ultimaData = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fimConsultaUtc, fusoHorario).DateTime);
        var horarios = new List<HorarioAgendaResponse>();
        var agoraUtc = DateTimeOffset.UtcNow;

        for (var data = primeiraData; data <= ultimaData; data = data.AddDays(1))
        {
            foreach (var faixa in faixas.Where(item => item.DiaSemana == data.DayOfWeek))
            {
                var inicioMinutos = (int)faixa.HoraInicio.ToTimeSpan().TotalMinutes;
                var fimMinutos = (int)faixa.HoraFim.ToTimeSpan().TotalMinutes;
                for (var minuto = inicioMinutos; minuto + DuracaoAulaMinutos <= fimMinutos; minuto += DuracaoAulaMinutos)
                {
                    var horaLocal = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minuto));
                    var dataHoraLocal = data.ToDateTime(horaLocal, DateTimeKind.Unspecified);
                    foreach (var inicioSlotUtc in ConverterHorarioLocalParaUtc(dataHoraLocal, fusoHorario))
                    {
                        var fimSlotUtc = inicioSlotUtc.AddMinutes(DuracaoAulaMinutos);
                        if (inicioSlotUtc <= agoraUtc
                            || inicioSlotUtc < inicioConsultaUtc
                            || fimSlotUtc > fimConsultaUtc)
                        {
                            continue;
                        }

                        var disponivel = !ocupadas.Any(aula => aula.InicioUtc < fimSlotUtc && aula.FimUtc > inicioSlotUtc);
                        horarios.Add(new(inicioSlotUtc, fimSlotUtc, quantidadePontos, disponivel));
                    }
                }
            }
        }

        return new(AgendaOperacaoStatus.Concluida,
            horarios.OrderBy(item => item.InicioUtc).ToArray());
    }

    public async Task<AgendaOperacaoResult<AulaAgendadaResponse>> ReservarAsync(
        int contaAcessoAlunoId,
        ReservarAulaRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProfessorId <= 0 || request.ChaveIdempotencia == Guid.Empty)
        {
            return new(AgendaOperacaoStatus.EntradaInvalida, null);
        }

        var inicioUtc = request.InicioUtc.ToUniversalTime();
        if (inicioUtc <= DateTimeOffset.UtcNow
            || inicioUtc > DateTimeOffset.UtcNow.AddDays(180))
        {
            return new(AgendaOperacaoStatus.EntradaInvalida, null);
        }

        var professor = await dbContext.Professores
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.ProfessorId && item.Ativo, cancellationToken);
        if (professor is null)
        {
            return new(AgendaOperacaoStatus.ProfessorNaoEncontrado, null);
        }

        if (!TryObterQuantidadePontos(professor.HoraAula, out var quantidadePontos))
        {
            return new(AgendaOperacaoStatus.TarifaInvalida, null);
        }

        var contaProfessorInfo = await dbContext.ContasAcesso
            .AsNoTracking()
            .SingleOrDefaultAsync(conta => conta.TipoPerfil == TipoProfessor && conta.PerfilId == professor.Id, cancellationToken);
        if (contaProfessorInfo is null)
        {
            return new(AgendaOperacaoStatus.ContaNaoEncontrada, null);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var idsContas = new[] { contaAcessoAlunoId, contaProfessorInfo.Id }.Distinct().Order().ToArray();
        var contasBloqueadas = new Dictionary<int, ContaAcesso>();
        foreach (var idConta in idsContas)
        {
            var contaBloqueada = await ObterContaBloqueadaAsync(idConta, cancellationToken);
            if (contaBloqueada is null)
            {
                return new(AgendaOperacaoStatus.ContaNaoEncontrada, null);
            }

            contasBloqueadas.Add(idConta, contaBloqueada);
        }

        var contaAluno = contasBloqueadas[contaAcessoAlunoId];
        var contaProfessor = contasBloqueadas[contaProfessorInfo.Id];
        if (contaAluno.TipoPerfil != TipoAluno || contaProfessor.TipoPerfil != TipoProfessor
            || contaProfessor.PerfilId != professor.Id)
        {
            return new(AgendaOperacaoStatus.PerfilIncorreto, null);
        }

        var chaveIdempotencia = request.ChaveIdempotencia.ToString("N");
        var aulaExistente = await dbContext.AulasAgendadas
            .AsNoTracking()
            .SingleOrDefaultAsync(aula => aula.ContaAcessoAlunoId == contaAcessoAlunoId
                && aula.ChaveIdempotencia == chaveIdempotencia, cancellationToken);
        if (aulaExistente is not null)
        {
            if (aulaExistente.ProfessorId != professor.Id || aulaExistente.InicioUtc != inicioUtc)
            {
                return new(AgendaOperacaoStatus.IdempotenciaReutilizada, null);
            }

            await transaction.CommitAsync(cancellationToken);
            return new(AgendaOperacaoStatus.Concluida, ToResponse(aulaExistente));
        }

        var faixas = await dbContext.DisponibilidadesSemanaisProfessores
            .AsNoTracking()
            .Where(item => item.ProfessorId == professor.Id)
            .ToListAsync(cancellationToken);
        var fusos = faixas.Select(item => item.FusoHorarioId).Distinct(StringComparer.Ordinal).ToArray();
        if (fusos.Length != 1 || !TryObterFusoHorario(fusos[0], out var fusoHorario))
        {
            return new(AgendaOperacaoStatus.HorarioIndisponivel, null);
        }

        var inicioLocal = TimeZoneInfo.ConvertTime(inicioUtc, fusoHorario);
        var horaLocal = new TimeOnly(inicioLocal.Hour, inicioLocal.Minute, inicioLocal.Second);
        if (inicioLocal.TimeOfDay.Ticks % TimeSpan.TicksPerMinute != 0
            || inicioLocal.Minute != 0
            || !faixas.Any(faixa => faixa.DiaSemana == inicioLocal.DayOfWeek
                && horaLocal >= faixa.HoraInicio
                && horaLocal.ToTimeSpan().Add(TimeSpan.FromMinutes(DuracaoAulaMinutos)) <= faixa.HoraFim.ToTimeSpan()))
        {
            return new(AgendaOperacaoStatus.HorarioIndisponivel, null);
        }

        var fimUtc = inicioUtc.AddMinutes(DuracaoAulaMinutos);
        var conflito = await dbContext.AulasAgendadas.AnyAsync(aula =>
            aula.Status != StatusAula.Concluida
            && aula.InicioUtc < fimUtc
            && aula.FimUtc > inicioUtc
            && (aula.ProfessorId == professor.Id
                || aula.ContaAcessoAlunoId == contaAcessoAlunoId), cancellationToken);
        if (conflito)
        {
            return new(AgendaOperacaoStatus.HorarioOcupado, null);
        }

        if (contaAluno.SaldoPontos < quantidadePontos)
        {
            return new(AgendaOperacaoStatus.SaldoInsuficiente, null);
        }

        if (contaProfessor.SaldoPontos > int.MaxValue - quantidadePontos)
        {
            return new(AgendaOperacaoStatus.SaldoMaximoAtingido, null);
        }

        var aula = new AulaAgendada(
            professor.Id,
            contaProfessor.Id,
            contaAluno.Id,
            inicioUtc,
            quantidadePontos,
            chaveIdempotencia);
        contaAluno.SaldoPontos -= quantidadePontos;
        contaProfessor.SaldoPontos += quantidadePontos;
        dbContext.AulasAgendadas.Add(aula);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.LancamentosPontos.Add(new LancamentoPontos(
                contaAluno.Id,
                -quantidadePontos,
                $"Aula:{aula.Id}:TransferenciaSaida",
                $"aula:{aula.Id}:saida"));
            dbContext.LancamentosPontos.Add(new LancamentoPontos(
                contaProfessor.Id,
                quantidadePontos,
                $"Aula:{aula.Id}:TransferenciaEntrada",
                $"aula:{aula.Id}:entrada"));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var aulaConcorrente = await dbContext.AulasAgendadas
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ContaAcessoAlunoId == contaAcessoAlunoId
                    && item.ChaveIdempotencia == chaveIdempotencia, cancellationToken);
            return aulaConcorrente is not null
                && aulaConcorrente.ProfessorId == professor.Id
                && aulaConcorrente.InicioUtc == inicioUtc
                    ? new(AgendaOperacaoStatus.Concluida, ToResponse(aulaConcorrente))
                    : new(AgendaOperacaoStatus.HorarioOcupado, null);
        }

        return new(AgendaOperacaoStatus.Concluida, ToResponse(aula));
    }

    public async Task<IReadOnlyList<AulaAgendadaResponse>> ListarMinhasAsync(
        int contaAcessoId,
        CancellationToken cancellationToken)
    {
        var conta = await dbContext.ContasAcesso
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == contaAcessoId, cancellationToken);
        if (conta is null)
        {
            return [];
        }

        var consulta = dbContext.AulasAgendadas.AsNoTracking().AsQueryable();
        consulta = conta.TipoPerfil switch
        {
            TipoAluno => consulta.Where(aula => aula.ContaAcessoAlunoId == contaAcessoId),
            TipoProfessor => consulta.Where(aula => aula.ContaAcessoProfessorId == contaAcessoId),
            _ => consulta.Where(_ => false)
        };

        var aulas = await consulta
            .OrderBy(aula => aula.InicioUtc)
            .ToListAsync(cancellationToken);
        return aulas.Select(ToResponse).ToArray();
    }

    public async Task<AulaAgendadaResponse?> ObterMinhaAulaAsync(
        int contaAcessoId,
        long aulaId,
        CancellationToken cancellationToken)
    {
        var aula = await dbContext.AulasAgendadas
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == aulaId
                && (item.ContaAcessoAlunoId == contaAcessoId || item.ContaAcessoProfessorId == contaAcessoId), cancellationToken);
        return aula is null ? null : ToResponse(aula);
    }

    private async Task<ContaAcesso?> ObterContaBloqueadaAsync(int contaAcessoId, CancellationToken cancellationToken) =>
        await dbContext.ContasAcesso
            .FromSqlInterpolated($"SELECT * FROM \"ContasAcesso\" WHERE \"Id\" = {contaAcessoId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static AgendaOperacaoStatus ValidarFaixas(AtualizarDisponibilidadeSemanalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FusoHorarioId) || request.FusoHorarioId.Trim().Length > 100
            || request.Faixas is null || request.Faixas.Count is 0 or > 50)
        {
            return AgendaOperacaoStatus.EntradaInvalida;
        }

        foreach (var grupo in request.Faixas.GroupBy(faixa => faixa.DiaSemana))
        {
            if ((int)grupo.Key is < 0 or > 6)
            {
                return AgendaOperacaoStatus.EntradaInvalida;
            }

            var ordenadas = grupo.OrderBy(faixa => faixa.HoraInicio).ToArray();
            for (var index = 0; index < ordenadas.Length; index++)
            {
                var faixa = ordenadas[index];
                var inicio = faixa.HoraInicio.ToTimeSpan();
                var fim = faixa.HoraFim.ToTimeSpan();
                if (fim <= inicio || inicio.Ticks % TimeSpan.TicksPerHour != 0
                    || fim.Ticks % TimeSpan.TicksPerHour != 0
                    || (fim - inicio).Ticks % TimeSpan.TicksPerHour != 0)
                {
                    return AgendaOperacaoStatus.EntradaInvalida;
                }

                if (index > 0 && ordenadas[index - 1].HoraFim > faixa.HoraInicio)
                {
                    return AgendaOperacaoStatus.EntradaInvalida;
                }
            }
        }

        return AgendaOperacaoStatus.Concluida;
    }

    private static bool TryObterQuantidadePontos(decimal valorHoraAula, out int quantidadePontos)
    {
        quantidadePontos = 0;
        if (valorHoraAula <= 0 || valorHoraAula != decimal.Truncate(valorHoraAula) || valorHoraAula > int.MaxValue)
        {
            return false;
        }

        quantidadePontos = decimal.ToInt32(valorHoraAula);
        return quantidadePontos > 0;
    }

    private static bool TryObterFusoHorario(string fusoHorarioId, out TimeZoneInfo fusoHorario)
    {
        try
        {
            fusoHorario = TimeZoneInfo.FindSystemTimeZoneById(fusoHorarioId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            fusoHorario = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            fusoHorario = TimeZoneInfo.Utc;
            return false;
        }
    }

    private static IEnumerable<DateTimeOffset> ConverterHorarioLocalParaUtc(DateTime horarioLocal, TimeZoneInfo fusoHorario)
    {
        if (fusoHorario.IsInvalidTime(horarioLocal))
        {
            return [];
        }

        if (fusoHorario.IsAmbiguousTime(horarioLocal))
        {
            return fusoHorario.GetAmbiguousTimeOffsets(horarioLocal)
                .Select(offset => new DateTimeOffset(horarioLocal, offset).ToUniversalTime());
        }

        return [new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(horarioLocal, fusoHorario), TimeSpan.Zero)];
    }

    private static AulaAgendadaResponse ToResponse(AulaAgendada aula) => new(
        aula.Id,
        aula.ProfessorId,
        aula.InicioUtc,
        aula.FimUtc,
        aula.QuantidadePontos,
        aula.Status.ToString());
}

public sealed record AgendaOperacaoResult<T>(AgendaOperacaoStatus Status, T? Valor = default);
public sealed record AgendaOperacaoResult(AgendaOperacaoStatus Status);

public enum AgendaOperacaoStatus
{
    Concluida,
    ContaNaoEncontrada,
    PerfilIncorreto,
    ProfessorNaoEncontrado,
    ProfessorInativo,
    EntradaInvalida,
    FusoHorarioInvalido,
    TarifaInvalida,
    HorarioIndisponivel,
    HorarioOcupado,
    SaldoInsuficiente,
    SaldoMaximoAtingido,
    IdempotenciaReutilizada
}
