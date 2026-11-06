using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.EntityFrameworkCore;
using professor.Model;
using Xunit;

namespace APILinguagem.Tests;

[Collection("PostgreSQL")]
public sealed class AgendaAulasServiceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ReservaTransferePontosUmaUnicaVezQuandoRepetidaAsync()
    {
        var cenario = await CriarCenarioAsync(saldoAluno: 100, saldoProfessor: 3);
        var chave = Guid.NewGuid();
        AgendaOperacaoResult<AulaAgendadaResponse> primeira;
        await using (var dbContext = fixture.CriarDbContext())
        {
            primeira = await new AgendaAulasService(dbContext).ReservarAsync(
                cenario.ContaAlunoId,
                new ReservarAulaRequest(cenario.ProfessorId, cenario.InicioUtc, chave),
                CancellationToken.None);
        }

        await using (var dbContext = fixture.CriarDbContext())
        {
            var repetida = await new AgendaAulasService(dbContext).ReservarAsync(
                cenario.ContaAlunoId,
                new ReservarAulaRequest(cenario.ProfessorId, cenario.InicioUtc, chave),
                CancellationToken.None);

            Assert.Equal(AgendaOperacaoStatus.Concluida, repetida.Status);
            Assert.Equal(primeira.Valor!.Id, repetida.Valor!.Id);
        }

        Assert.Equal(AgendaOperacaoStatus.Concluida, primeira.Status);
        await AssertContaAsync(cenario.ContaAlunoId, saldoEsperado: 40, quantidadeLancamentos: 1, quantidadeTransferida: -60);
        await AssertContaAsync(cenario.ContaProfessorId, saldoEsperado: 63, quantidadeLancamentos: 1, quantidadeTransferida: 60);
        await using var verificacao = fixture.CriarDbContext();
        Assert.Equal(1, await verificacao.AulasAgendadas.CountAsync(aula => aula.ProfessorId == cenario.ProfessorId));
    }

    [Fact]
    public async Task RecusaHorarioForaDaGradeDeHoraCheiaAsync()
    {
        var cenario = await CriarCenarioAsync(saldoAluno: 100, saldoProfessor: 0);
        await using var dbContext = fixture.CriarDbContext();

        var resultado = await new AgendaAulasService(dbContext).ReservarAsync(
            cenario.ContaAlunoId,
            new ReservarAulaRequest(cenario.ProfessorId, cenario.InicioUtc.AddMinutes(30), Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AgendaOperacaoStatus.HorarioIndisponivel, resultado.Status);
        await AssertContaAsync(cenario.ContaAlunoId, saldoEsperado: 100, quantidadeLancamentos: 0, quantidadeTransferida: 0);
        await AssertContaAsync(cenario.ContaProfessorId, saldoEsperado: 0, quantidadeLancamentos: 0, quantidadeTransferida: 0);
    }

    [Fact]
    public async Task SaldoInsuficienteNaoCriaAulaNemTransferePontosAsync()
    {
        var cenario = await CriarCenarioAsync(saldoAluno: 59, saldoProfessor: 0);
        await using var dbContext = fixture.CriarDbContext();

        var resultado = await new AgendaAulasService(dbContext).ReservarAsync(
            cenario.ContaAlunoId,
            new ReservarAulaRequest(cenario.ProfessorId, cenario.InicioUtc, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AgendaOperacaoStatus.SaldoInsuficiente, resultado.Status);
        await AssertContaAsync(cenario.ContaAlunoId, saldoEsperado: 59, quantidadeLancamentos: 0, quantidadeTransferida: 0);
        await AssertContaAsync(cenario.ContaProfessorId, saldoEsperado: 0, quantidadeLancamentos: 0, quantidadeTransferida: 0);
        await using var verificacao = fixture.CriarDbContext();
        Assert.False(await verificacao.AulasAgendadas.AnyAsync(aula => aula.ProfessorId == cenario.ProfessorId));
    }

    [Fact]
    public async Task ListaSomenteOsHorariosAlinhadosDisponiveisAsync()
    {
        var cenario = await CriarCenarioAsync(saldoAluno: 100, saldoProfessor: 0);
        await using var dbContext = fixture.CriarDbContext();

        var resultado = await new AgendaAulasService(dbContext).ListarHorariosDisponiveisAsync(
            cenario.ProfessorId,
            cenario.InicioUtc.AddHours(-1),
            cenario.InicioUtc.AddHours(3),
            CancellationToken.None);

        Assert.Equal(AgendaOperacaoStatus.Concluida, resultado.Status);
        Assert.Equal(3, resultado.Valor!.Count);
        Assert.All(resultado.Valor, horario => Assert.Equal(60, horario.QuantidadePontos));
        Assert.All(resultado.Valor, horario => Assert.Equal(0, TimeZoneInfo.ConvertTime(horario.InicioUtc, TimeZoneInfo.Utc).Minute));
    }

    [Fact]
    public async Task AgendaMarcaHorarioReservadoComoOcupadoAsync()
    {
        var cenario = await CriarCenarioAsync(saldoAluno: 100, saldoProfessor: 0);
        var reserva = await ReservarEmNovoContextoAsync(cenario.ContaAlunoId, cenario.ProfessorId, cenario.InicioUtc);
        Assert.Equal(AgendaOperacaoStatus.Concluida, reserva.Status);

        await using var dbContext = fixture.CriarDbContext();
        var service = new AgendaAulasService(dbContext);
        var resultado = await service.ListarAgendaProfessorAsync(
            cenario.ProfessorId,
            cenario.InicioUtc.AddHours(-1),
            cenario.InicioUtc.AddHours(2),
            CancellationToken.None);

        Assert.Equal(AgendaOperacaoStatus.Concluida, resultado.Status);
        Assert.Collection(
            resultado.Valor!,
            horario => Assert.True(horario.Disponivel),
            horario => Assert.False(horario.Disponivel),
            horario => Assert.True(horario.Disponivel));

        var horariosLivres = await service.ListarHorariosDisponiveisAsync(
            cenario.ProfessorId,
            cenario.InicioUtc.AddHours(-1),
            cenario.InicioUtc.AddHours(2),
            CancellationToken.None);
        Assert.Equal(2, horariosLivres.Valor!.Count);
    }

    [Fact]
    public async Task DisponibilidadeSalvaPelaContaDoProfessorApareceNaAgendaDoMesmoPerfilAsync()
    {
        var cenario = await CriarCenarioAsync(saldoAluno: 100, saldoProfessor: 0);
        await using var dbContext = fixture.CriarDbContext();
        var service = new AgendaAulasService(dbContext);
        var atualizacao = await service.DefinirDisponibilidadeAsync(
            cenario.ContaProfessorId,
            new AtualizarDisponibilidadeSemanalRequest(
                "UTC",
                [new FaixaDisponibilidadeRequest(DayOfWeek.Monday, new TimeOnly(13, 0), new TimeOnly(16, 0))]),
            CancellationToken.None);

        Assert.Equal(AgendaOperacaoStatus.Concluida, atualizacao.Status);
        var agenda = await service.ListarAgendaProfessorAsync(
            cenario.ProfessorId,
            cenario.InicioUtc.AddHours(-1),
            cenario.InicioUtc.AddHours(3),
            CancellationToken.None);

        Assert.Equal(AgendaOperacaoStatus.Concluida, agenda.Status);
        Assert.Equal(3, agenda.Valor!.Count);
        Assert.All(agenda.Valor, horario => Assert.True(horario.Disponivel));
        Assert.All(agenda.Valor, horario => Assert.Equal(60, horario.QuantidadePontos));

        var minhaDisponibilidade = await service.ObterMinhaDisponibilidadeSemanalAsync(
            cenario.ContaProfessorId, CancellationToken.None);
        Assert.Equal(AgendaOperacaoStatus.Concluida, minhaDisponibilidade.Status);
        Assert.Equal("UTC", minhaDisponibilidade.Valor!.FusoHorarioId);
        Assert.Single(minhaDisponibilidade.Valor.Faixas);

        var minhaAgenda = await service.ListarMinhaAgendaAsync(
            cenario.ContaProfessorId,
            cenario.InicioUtc.AddHours(-1),
            cenario.InicioUtc.AddHours(3),
            CancellationToken.None);
        Assert.Equal(AgendaOperacaoStatus.Concluida, minhaAgenda.Status);
        Assert.Equal(3, minhaAgenda.Valor!.Count);
        Assert.All(minhaAgenda.Valor, horario => Assert.True(horario.Disponivel));
    }

    [Fact]
    public async Task AtualizacaoVaziaNaoApagaDisponibilidadeExistenteAsync()
    {
        var cenario = await CriarCenarioAsync(saldoAluno: 100, saldoProfessor: 0);
        await using var dbContext = fixture.CriarDbContext();

        var resultado = await new AgendaAulasService(dbContext).DefinirDisponibilidadeAsync(
            cenario.ContaProfessorId,
            new AtualizarDisponibilidadeSemanalRequest("UTC", []),
            CancellationToken.None);

        Assert.Equal(AgendaOperacaoStatus.EntradaInvalida, resultado.Status);
        Assert.Equal(1, await dbContext.DisponibilidadesSemanaisProfessores
            .CountAsync(item => item.ProfessorId == cenario.ProfessorId));
    }

    [Fact]
    public async Task LimpezaExplicitaRemoveDisponibilidadeDoProfessorAutenticadoAsync()
    {
        var cenario = await CriarCenarioAsync(saldoAluno: 100, saldoProfessor: 0);
        await using var dbContext = fixture.CriarDbContext();

        var resultado = await new AgendaAulasService(dbContext).LimparDisponibilidadeAsync(
            cenario.ContaProfessorId, CancellationToken.None);

        Assert.Equal(AgendaOperacaoStatus.Concluida, resultado);
        Assert.Empty(await dbContext.DisponibilidadesSemanaisProfessores
            .Where(item => item.ProfessorId == cenario.ProfessorId)
            .ToListAsync());
    }

    [Fact]
    public async Task DuasReservasConcorrentesDaMesmaVagaTransferemPontosUmaVezAsync()
    {
        var cenario = await CriarCenarioAsync(saldoAluno: 100, saldoProfessor: 0);
        var segundoAlunoId = await CriarOutroAlunoAsync();
        var primeiraReserva = ReservarEmNovoContextoAsync(cenario.ContaAlunoId, cenario.ProfessorId, cenario.InicioUtc);
        var segundaReserva = ReservarEmNovoContextoAsync(segundoAlunoId, cenario.ProfessorId, cenario.InicioUtc);

        var resultados = await Task.WhenAll(primeiraReserva, segundaReserva);
        Assert.Single(resultados, resultado => resultado.Status == AgendaOperacaoStatus.Concluida);
        Assert.Single(resultados, resultado => resultado.Status == AgendaOperacaoStatus.HorarioOcupado);

        var alunoVencedorId = resultados[0].Status == AgendaOperacaoStatus.Concluida
            ? cenario.ContaAlunoId
            : segundoAlunoId;
        var alunoSemReservaId = alunoVencedorId == cenario.ContaAlunoId
            ? segundoAlunoId
            : cenario.ContaAlunoId;
        await AssertContaAsync(alunoVencedorId, saldoEsperado: 40, quantidadeLancamentos: 1, quantidadeTransferida: -60);
        await AssertContaAsync(alunoSemReservaId, saldoEsperado: 100, quantidadeLancamentos: 0, quantidadeTransferida: 0);
        await AssertContaAsync(cenario.ContaProfessorId, saldoEsperado: 60, quantidadeLancamentos: 1, quantidadeTransferida: 60);
    }

    private async Task<Cenario> CriarCenarioAsync(int saldoAluno, int saldoProfessor)
    {
        var sufixo = Guid.NewGuid().ToString("N");
        await using var dbContext = fixture.CriarDbContext();
        var professor = new Professores
        {
            Nome = "Professor de teste",
            Email = $"professor-{sufixo}@example.test",
            Telefone = "11999999999",
            Pais = "Brasil",
            Estado = "SP",
            Cidade = "São Paulo",
            LinguaDeEnsino = "Português",
            HoraAula = 60,
            Ativo = true,
            TermosAceitos = true
        };
        var aluno = new Alunos
        {
            Nome = "Aluno de teste",
            Email = $"aluno-{sufixo}@example.test",
            Telefone = "11999999998",
            Pais = "Brasil",
            Estado = "SP",
            Cidade = "São Paulo",
            LinguaDeInteresse = "Português",
            TermosAceitos = true
        };
        dbContext.Professores.Add(professor);
        dbContext.Alunos.Add(aluno);
        await dbContext.SaveChangesAsync();

        var contaProfessor = CriarConta($"professor-{sufixo}@example.test", "professor", professor.Id, saldoProfessor);
        var contaAluno = CriarConta($"aluno-{sufixo}@example.test", "aluno", aluno.Id, saldoAluno);
        dbContext.ContasAcesso.AddRange(contaProfessor, contaAluno);
        dbContext.DisponibilidadesSemanaisProfessores.Add(new DisponibilidadeSemanalProfessor
        {
            ProfessorId = professor.Id,
            DiaSemana = DayOfWeek.Monday,
            HoraInicio = new TimeOnly(13, 0),
            HoraFim = new TimeOnly(16, 0),
            FusoHorarioId = "UTC"
        });
        await dbContext.SaveChangesAsync();

        var data = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        data = data.AddDays(((int)DayOfWeek.Monday - (int)data.DayOfWeek + 7) % 7);
        var inicio = new DateTimeOffset(data.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc));
        return new Cenario(professor.Id, contaProfessor.Id, contaAluno.Id, inicio);
    }

    private static ContaAcesso CriarConta(string email, string tipoPerfil, int perfilId, int saldo) => new()
    {
        Email = email,
        EmailNormalizado = email.ToUpperInvariant(),
        Nome = tipoPerfil,
        TipoPerfil = tipoPerfil,
        PerfilId = perfilId,
        SenhaHash = "hash-de-teste",
        SaldoPontos = saldo
    };

    private async Task<int> CriarOutroAlunoAsync()
    {
        var sufixo = Guid.NewGuid().ToString("N");
        await using var dbContext = fixture.CriarDbContext();
        var aluno = new Alunos
        {
            Nome = "Segundo aluno de teste",
            Email = $"aluno-{sufixo}@example.test",
            Telefone = "11999999997",
            Pais = "Brasil",
            Estado = "SP",
            Cidade = "São Paulo",
            LinguaDeInteresse = "Português",
            TermosAceitos = true
        };
        dbContext.Alunos.Add(aluno);
        await dbContext.SaveChangesAsync();
        var conta = CriarConta(aluno.Email, "aluno", aluno.Id, saldo: 100);
        dbContext.ContasAcesso.Add(conta);
        await dbContext.SaveChangesAsync();
        return conta.Id;
    }

    private async Task<AgendaOperacaoResult<AulaAgendadaResponse>> ReservarEmNovoContextoAsync(
        int contaAlunoId,
        int professorId,
        DateTimeOffset inicioUtc)
    {
        await using var dbContext = fixture.CriarDbContext();
        return await new AgendaAulasService(dbContext).ReservarAsync(
            contaAlunoId,
            new ReservarAulaRequest(professorId, inicioUtc, Guid.NewGuid()),
            CancellationToken.None);
    }

    private async Task AssertContaAsync(int contaId, int saldoEsperado, int quantidadeLancamentos, int quantidadeTransferida)
    {
        await using var dbContext = fixture.CriarDbContext();
        var conta = await dbContext.ContasAcesso.SingleAsync(item => item.Id == contaId);
        var lancamentos = await dbContext.LancamentosPontos
            .Where(item => item.ContaAcessoId == contaId)
            .ToListAsync();

        Assert.Equal(saldoEsperado, conta.SaldoPontos);
        Assert.Equal(quantidadeLancamentos, lancamentos.Count);
        Assert.Equal(quantidadeTransferida, lancamentos.Sum(item => item.Quantidade));
    }

    private sealed record Cenario(
        int ProfessorId,
        int ContaProfessorId,
        int ContaAlunoId,
        DateTimeOffset InicioUtc);
}
