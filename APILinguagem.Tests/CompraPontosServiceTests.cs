using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace APILinguagem.Tests;

[Collection("PostgreSQL")]
public sealed class CompraPontosServiceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task PagamentoConfirmadoCreditaCompraUmaUnicaVezAsync()
    {
        var (contaId, compraId, txid) = await CriarCompraAsync();
        var inter = new FakeInterPixClient(new InterPixCobranca(
            txid,
            "CONCLUIDA",
            "12.50",
            [new InterPixPagamento(txid, "12.50")]));

        await using var dbContext = fixture.CriarDbContext();
        var service = CriarService(dbContext, inter);

        Assert.True(await service.ConfirmarPagamentoAsync(txid, CancellationToken.None));
        Assert.True(await service.ConfirmarPagamentoAsync(txid, CancellationToken.None));
        await fixture.AssertEstadoAsync(contaId, 20, 1);

        var compra = await dbContext.ComprasPontos.AsNoTracking().SingleAsync(item => item.Id == compraId);
        Assert.Equal(CompraPontosStatus.Paga, compra.Status);
        Assert.NotNull(compra.DataPagamento);
        Assert.Equal(1, inter.Consultas);
    }

    [Theory]
    [InlineData("CONCLUIDA", "12.49", "12.49")]
    [InlineData("ATIVA", "12.50", "12.50")]
    [InlineData("CONCLUIDA", "12.50", "12.00")]
    public async Task PagamentoNaoCorrespondenteNaoCreditaPontosAsync(
        string status,
        string valorCobranca,
        string valorPago)
    {
        var (contaId, _, txid) = await CriarCompraAsync();
        var inter = new FakeInterPixClient(new InterPixCobranca(
            txid,
            status,
            valorCobranca,
            [new InterPixPagamento(txid, valorPago)]));

        await using var dbContext = fixture.CriarDbContext();
        var service = CriarService(dbContext, inter);

        Assert.False(await service.ConfirmarPagamentoAsync(txid, CancellationToken.None));
        await fixture.AssertEstadoAsync(contaId, 0, 0);
    }

    [Fact]
    public async Task ConfirmacoesConcorrentesNaoDuplicamCreditoAsync()
    {
        var (contaId, _, txid) = await CriarCompraAsync();
        var inter = new FakeInterPixClient(new InterPixCobranca(
            txid,
            "CONCLUIDA",
            "12.50",
            [new InterPixPagamento(txid, "12.50")]));
        await using var dbContext1 = fixture.CriarDbContext();
        await using var dbContext2 = fixture.CriarDbContext();

        var resultados = await Task.WhenAll(
            CriarService(dbContext1, inter).ConfirmarPagamentoAsync(txid, CancellationToken.None),
            CriarService(dbContext2, inter).ConfirmarPagamentoAsync(txid, CancellationToken.None));

        Assert.All(resultados, resultado => Assert.True(resultado));
        await fixture.AssertEstadoAsync(contaId, 20, 1);
    }

    private async Task<(int ContaId, long CompraId, string Txid)> CriarCompraAsync()
    {
        await using var dbContext = fixture.CriarDbContext();
        var identificador = Guid.NewGuid().ToString("N");
        var conta = new ContaAcesso
        {
            Email = $"{identificador}@example.test",
            EmailNormalizado = $"{identificador}@EXAMPLE.TEST",
            Nome = "Aluna de teste",
            TipoPerfil = "aluno",
            PerfilId = Random.Shared.Next(1, int.MaxValue),
            SenhaHash = "hash-de-teste",
            SaldoPontos = 0
        };
        var pacote = new PacotePontos("Pacote de teste " + identificador, 20, 1250);
        dbContext.AddRange(conta, pacote);
        await dbContext.SaveChangesAsync();

        var txid = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var compra = new CompraPontos(
            conta.Id,
            pacote.Id,
            pacote.QuantidadePontos,
            pacote.PrecoCentavos,
            txid,
            CompraPontosStatus.Pendente,
            DateTimeOffset.UtcNow.AddMinutes(20));
        dbContext.ComprasPontos.Add(compra);
        await dbContext.SaveChangesAsync();
        return (conta.Id, compra.Id, txid);
    }

    private static CompraPontosService CriarService(AlunosDbContext dbContext, IInterPixClient inter) => new(
        dbContext,
        inter,
        Options.Create(new InterPixOptions()),
        new TestHostEnvironment(),
        NullLogger<CompraPontosService>.Instance);

    private sealed class FakeInterPixClient(InterPixCobranca cobranca) : IInterPixClient
    {
        public int Consultas { get; private set; }

        public Task<InterPixCriacaoResult> CriarCobrancaAsync(
            string txid,
            int precoCentavos,
            int expiracaoSegundos,
            string descricao,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InterPixCobranca> ConsultarCobrancaAsync(string txid, CancellationToken cancellationToken)
        {
            Consultas++;
            return Task.FromResult(cobranca);
        }

        public Task ConfigurarWebhookAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "APILinguagem.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}