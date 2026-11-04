using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace APILinguagem.Tests;

[Collection("PostgreSQL")]
public sealed class PesquisaPontosServiceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task SaldoInsuficienteNaoExecutaPesquisaNemCriaLancamentoAsync()
    {
        var contaId = await fixture.CriarContaAsync(0);
        await using var dbContext = fixture.CriarDbContext();
        var pesquisaExecutada = false;
        var service = new PesquisaPontosService(dbContext);

        var resultado = await service.ExecutarAsync(
            contaId,
            Guid.NewGuid().ToString("N"),
            Impressao("sem-saldo"),
            _ =>
            {
                pesquisaExecutada = true;
                return Task.FromResult<IReadOnlyList<int>>([1]);
            },
            CancellationToken.None);

        Assert.Equal(PesquisaTransacaoStatus.SaldoInsuficiente, resultado.Status);
        Assert.False(pesquisaExecutada);
        await fixture.AssertEstadoAsync(contaId, 0, 0);
    }

    [Fact]
    public async Task PesquisasSimultaneasNaoDebitamAlemDoSaldoAsync()
    {
        var contaId = await fixture.CriarContaAsync(1);
        var impressao = Impressao("concorrente");
        var chave = Guid.NewGuid().ToString("N");
        await using var dbContext1 = fixture.CriarDbContext();
        await using var dbContext2 = fixture.CriarDbContext();
        var service1 = new PesquisaPontosService(dbContext1);
        var service2 = new PesquisaPontosService(dbContext2);
        var primeiraPesquisaIniciada = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberarPrimeiraPesquisa = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var segundaPesquisaExecutada = false;

        var primeira = service1.ExecutarAsync(
            contaId,
            chave,
            impressao,
            async cancellationToken =>
            {
                primeiraPesquisaIniciada.SetResult(true);
                await liberarPrimeiraPesquisa.Task.WaitAsync(cancellationToken);
                return (IReadOnlyList<int>)[1];
            },
            CancellationToken.None);

        await primeiraPesquisaIniciada.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var segundaIniciada = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var segunda = Task.Run(async () =>
        {
            segundaIniciada.SetResult(true);
            return await service2.ExecutarAsync(
                contaId,
                Guid.NewGuid().ToString("N"),
                Impressao("concorrente-2"),
                _ =>
                {
                    segundaPesquisaExecutada = true;
                    return Task.FromResult<IReadOnlyList<int>>([2]);
                },
                CancellationToken.None);
        });

        await segundaIniciada.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);
        liberarPrimeiraPesquisa.SetResult(true);

        var resultados = await Task.WhenAll(primeira, segunda);

        Assert.Contains(resultados, item => item.Status == PesquisaTransacaoStatus.Concluida);
        Assert.Contains(resultados, item => item.Status == PesquisaTransacaoStatus.SaldoInsuficiente);
        Assert.False(segundaPesquisaExecutada);
        await fixture.AssertEstadoAsync(contaId, 0, 1);
    }

    [Fact]
    public async Task RepetirMesmaRequisicaoNaoDebitaNovamenteAsync()
    {
        var contaId = await fixture.CriarContaAsync(2);
        var chave = Guid.NewGuid().ToString("N");
        var impressao = Impressao("retry");
        var chamadasPesquisa = 0;

        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            await using var dbContext = fixture.CriarDbContext();
            var service = new PesquisaPontosService(dbContext);
            var resultado = await service.ExecutarAsync(
                contaId,
                chave,
                impressao,
                _ =>
                {
                    chamadasPesquisa++;
                    return Task.FromResult<IReadOnlyList<int>>([7]);
                },
                CancellationToken.None);

            Assert.Equal(PesquisaTransacaoStatus.Concluida, resultado.Status);
        }

        Assert.Equal(2, chamadasPesquisa);
        await fixture.AssertEstadoAsync(contaId, 1, 1);
    }

    [Fact]
    public async Task ErroNaPesquisaMantemSaldoELancamentosIntactosAsync()
    {
        var contaId = await fixture.CriarContaAsync(1);
        await using var dbContext = fixture.CriarDbContext();
        var service = new PesquisaPontosService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecutarAsync<int>(
            contaId,
            Guid.NewGuid().ToString("N"),
            Impressao("erro"),
            _ => Task.FromException<IReadOnlyList<int>>(new InvalidOperationException("Falha simulada na pesquisa.")),
            CancellationToken.None));

        await fixture.AssertEstadoAsync(contaId, 1, 0);
    }

    [Fact]
    public async Task DebitoELancamentoPermanecemConsistentesAsync()
    {
        var saldoInicial = 3;
        var contaId = await fixture.CriarContaAsync(saldoInicial);
        await using var dbContext = fixture.CriarDbContext();
        var service = new PesquisaPontosService(dbContext);

        var resultado = await service.ExecutarAsync(
            contaId,
            Guid.NewGuid().ToString("N"),
            Impressao("consistencia"),
            _ => Task.FromResult<IReadOnlyList<int>>([11]),
            CancellationToken.None);

        Assert.Equal(PesquisaTransacaoStatus.Concluida, resultado.Status);
        Assert.Equal(saldoInicial - PesquisaPontosService.CustoPesquisa, resultado.SaldoPontos);
        await fixture.AssertEstadoAsync(contaId, saldoInicial - 1, 1);
        await fixture.AssertSaldoMaisLancamentosAsync(contaId, saldoInicial);
    }

    private static string Impressao(string value) => PesquisaPontosService.CriarImpressaoPesquisa(new { value });
}

[CollectionDefinition("PostgreSQL")]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("professor_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var dbContext = CriarDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AlunosDbContext CriarDbContext()
    {
        var options = new DbContextOptionsBuilder<AlunosDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AlunosDbContext(options);
    }

    public async Task<int> CriarContaAsync(int saldo)
    {
        await using var dbContext = CriarDbContext();
        var identificador = Guid.NewGuid().ToString("N");
        var conta = new ContaAcesso
        {
            Email = $"{identificador}@example.test",
            EmailNormalizado = $"{identificador}@EXAMPLE.TEST",
            Nome = "Conta de teste",
            TipoPerfil = "teste",
            PerfilId = Random.Shared.Next(1, int.MaxValue),
            SenhaHash = "hash-de-teste",
            SaldoPontos = saldo
        };

        dbContext.ContasAcesso.Add(conta);
        await dbContext.SaveChangesAsync();
        return conta.Id;
    }

    public async Task AssertEstadoAsync(int contaId, int saldoEsperado, int quantidadeLancamentosEsperada)
    {
        await using var dbContext = CriarDbContext();
        var saldo = await dbContext.ContasAcesso
            .Where(conta => conta.Id == contaId)
            .Select(conta => conta.SaldoPontos)
            .SingleAsync();
        var quantidadeLancamentos = await dbContext.LancamentosPontos
            .CountAsync(lancamento => lancamento.ContaAcessoId == contaId);

        Assert.Equal(saldoEsperado, saldo);
        Assert.Equal(quantidadeLancamentosEsperada, quantidadeLancamentos);
    }

    public async Task AssertSaldoMaisLancamentosAsync(int contaId, int saldoInicial)
    {
        await using var dbContext = CriarDbContext();
        var saldoAtual = await dbContext.ContasAcesso
            .Where(conta => conta.Id == contaId)
            .Select(conta => conta.SaldoPontos)
            .SingleAsync();
        var totalLancamentos = await dbContext.LancamentosPontos
            .Where(lancamento => lancamento.ContaAcessoId == contaId)
            .SumAsync(lancamento => lancamento.Quantidade);

        Assert.Equal(saldoInicial, saldoAtual - totalLancamentos);
    }
}
