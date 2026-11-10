using System.Globalization;
using APILinguagem.Data;
using APILinguagem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace APILinguagem.Services;

public sealed class CompraPontosService(
    AlunosDbContext dbContext,
    IInterPixClient interPixClient,
    IOptions<InterPixOptions> options,
    IHostEnvironment environment,
    ILogger<CompraPontosService> logger)
{
    private const int TamanhoLoteReconciliacao = 25;

    public async Task<CompraPontosResponse?> CriarAsync(
        int contaAcessoId,
        int pacotePontosId,
        CancellationToken cancellationToken)
    {
        var conta = await dbContext.ContasAcesso
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == contaAcessoId, cancellationToken);
        if (conta is null || conta.TipoPerfil != "aluno")
        {
            return null;
        }

        var pacote = await dbContext.PacotesPontos
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == pacotePontosId && item.Ativo, cancellationToken);
        if (pacote is null)
        {
            return null;
        }

        options.Value.Validate(environment.ContentRootPath);
        var agora = DateTimeOffset.UtcNow;
        var txid = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var compra = new CompraPontos(
            contaAcessoId,
            pacote.Id,
            pacote.QuantidadePontos,
            pacote.PrecoCentavos,
            txid,
            CompraPontosStatus.Pendente,
            agora.AddMinutes(options.Value.ExpirationMinutes));

        dbContext.ComprasPontos.Add(compra);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CriarPixAsync(compra, cancellationToken);
        return ToResponse(compra);
    }

    public async Task<CompraPontosResponse?> RecriarPixAsync(
        int contaAcessoId,
        long compraId,
        CancellationToken cancellationToken)
    {
        var compra = await dbContext.ComprasPontos.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == compraId && item.ContaAcessoId == contaAcessoId,
            cancellationToken);
        if (compra is null)
        {
            return null;
        }

        if (compra.Status != CompraPontosStatus.Pendente)
        {
            return null;
        }

        if (compra.ExpiraEm <= DateTimeOffset.UtcNow)
        {
            if (!await ConfirmarPagamentoAsync(compra.IdentificadorProvedor, cancellationToken))
            {
                await MarcarComoExpiradaAsync(compra.Id, cancellationToken);
            }
            return await ObterAsync(contaAcessoId, compraId, cancellationToken);
        }

        var compraRastreada = await dbContext.ComprasPontos.SingleAsync(item => item.Id == compraId, cancellationToken);
        await CriarPixAsync(compraRastreada, cancellationToken);
        compra = compraRastreada;
        return ToResponse(compra);
    }

    public async Task<CompraPontosResponse?> ObterAsync(
        int contaAcessoId,
        long compraId,
        CancellationToken cancellationToken)
    {
        var compra = await dbContext.ComprasPontos.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == compraId && item.ContaAcessoId == contaAcessoId,
            cancellationToken);
        if (compra is null)
        {
            return null;
        }

        if (compra.Status == CompraPontosStatus.Pendente && compra.ExpiraEm <= DateTimeOffset.UtcNow)
        {
            if (!await ConfirmarPagamentoAsync(compra.IdentificadorProvedor, cancellationToken))
            {
                await MarcarComoExpiradaAsync(compra.Id, cancellationToken);
            }

            compra = await dbContext.ComprasPontos.AsNoTracking()
                .SingleAsync(item => item.Id == compraId, cancellationToken);
        }

        return ToResponse(compra);
    }

    public async Task<bool> ConfirmarPagamentoAsync(string txid, CancellationToken cancellationToken)
    {
        var compraInicial = await dbContext.ComprasPontos
            .AsNoTracking()
            .SingleOrDefaultAsync(compra => compra.IdentificadorProvedor == txid, cancellationToken);
        if (compraInicial is null)
        {
            return false;
        }
        if (compraInicial.Status == CompraPontosStatus.Paga)
        {
            return true;
        }
        if (compraInicial.Status == CompraPontosStatus.Cancelada)
        {
            return false;
        }

        var cobranca = await interPixClient.ConsultarCobrancaAsync(txid, cancellationToken);
        if (!PagamentoCorresponde(compraInicial, cobranca, txid))
        {
            return false;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var compra = await dbContext.ComprasPontos
            .FromSqlInterpolated($"SELECT * FROM \"ComprasPontos\" WHERE \"IdentificadorProvedor\" = {txid} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (compra is null || compra.Status is CompraPontosStatus.Paga or CompraPontosStatus.Cancelada)
        {
            await transaction.CommitAsync(cancellationToken);
            return compra?.Status == CompraPontosStatus.Paga;
        }

        if (!PagamentoCorresponde(compra, cobranca, txid))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var conta = await dbContext.ContasAcesso
            .FromSqlInterpolated($"SELECT * FROM \"ContasAcesso\" WHERE \"Id\" = {compra.ContaAcessoId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (conta is null)
        {
            throw new InvalidOperationException("A conta associada à compra não existe.");
        }

        var chaveLancamento = $"compra-pix:{compra.Id}";
        var lancamentoExistente = await dbContext.LancamentosPontos.AnyAsync(
            lancamento => lancamento.ContaAcessoId == conta.Id && lancamento.ChaveIdempotencia == chaveLancamento,
            cancellationToken);
        if (!lancamentoExistente)
        {
            var novoSaldo = (long)conta.SaldoPontos + compra.QuantidadePontos;
            if (novoSaldo > int.MaxValue)
            {
                throw new InvalidOperationException("O saldo máximo de pontos seria excedido por esta compra.");
            }

            conta.SaldoPontos = (int)novoSaldo;
            dbContext.LancamentosPontos.Add(new LancamentoPontos(
                conta.Id,
                compra.QuantidadePontos,
                $"Compra Pix #{compra.Id}",
                chaveLancamento));
        }

        compra.MarcarComoPaga(ObterDataPagamento(compra, cobranca) ?? DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task ReconciliarPendentesAsync(CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        var compras = await dbContext.ComprasPontos
            .AsNoTracking()
            .Where(compra => compra.Status == CompraPontosStatus.Pendente)
            .OrderBy(compra => compra.CriadaEm)
            .Take(TamanhoLoteReconciliacao)
            .Select(compra => new { compra.Id, compra.IdentificadorProvedor, compra.ExpiraEm })
            .ToListAsync(cancellationToken);

        foreach (var compra in compras)
        {
            try
            {
                var paga = await ConfirmarPagamentoAsync(compra.IdentificadorProvedor, cancellationToken);
                if (!paga && compra.ExpiraEm <= agora)
                {
                    await MarcarComoExpiradaAsync(compra.Id, cancellationToken);
                }
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "Falha ao consultar no Inter a compra Pix {CompraId}.", compra.Id);
            }
            catch (InvalidDataException exception)
            {
                logger.LogError(exception, "Resposta inválida do Inter para a compra Pix {CompraId}.", compra.Id);
            }
        }
    }

    private async Task MarcarComoExpiradaAsync(long compraId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var compra = await dbContext.ComprasPontos
            .FromSqlInterpolated($"SELECT * FROM \"ComprasPontos\" WHERE \"Id\" = {compraId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (compra is not null
            && compra.Status == CompraPontosStatus.Pendente
            && compra.ExpiraEm <= DateTimeOffset.UtcNow)
        {
            dbContext.ComprasPontos.Attach(compra);
            compra.MarcarComoExpirada();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task ConfigurarWebhookAsync(CancellationToken cancellationToken) =>
        interPixClient.ConfigurarWebhookAsync(cancellationToken);

    private async Task CriarPixAsync(CompraPontos compra, CancellationToken cancellationToken)
    {
        var expiracaoSegundos = checked((int)Math.Ceiling((compra.ExpiraEm - DateTimeOffset.UtcNow).TotalSeconds));
        if (expiracaoSegundos <= 0)
        {
            compra.MarcarComoExpirada();
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("O prazo para gerar esta cobrança Pix expirou.");
        }

        try
        {
            var dadosPix = await interPixClient.CriarCobrancaAsync(
                compra.IdentificadorProvedor,
                compra.PrecoCentavos,
                expiracaoSegundos,
                $"Compra de {compra.QuantidadePontos} pontos",
                cancellationToken);
            compra.DefinirDadosPix(
                dadosPix.IdentificadorCobranca,
                dadosPix.PixCopiaECola,
                dadosPix.QrCodeBase64);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new FalhaGerarPixException(compra.Id, exception);
        }
        catch (InvalidDataException exception)
        {
            throw new FalhaGerarPixException(compra.Id, exception);
        }
    }

    private static bool PagamentoCorresponde(CompraPontos compra, InterPixCobranca cobranca, string txid)
    {
        return string.Equals(cobranca.Txid, txid, StringComparison.Ordinal)
            && string.Equals(cobranca.Status, "CONCLUIDA", StringComparison.Ordinal)
            && ValorCorresponde(cobranca.ValorOriginal, compra.PrecoCentavos)
            && cobranca.Pagamentos.Any(pagamento =>
                string.Equals(pagamento.Txid, compra.IdentificadorProvedor, StringComparison.Ordinal)
                && ValorCorresponde(pagamento.Valor, compra.PrecoCentavos));
    }

    private static bool ValorCorresponde(string? valorOriginal, int precoCentavos) =>
        decimal.TryParse(valorOriginal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor)
        && valor * 100m == precoCentavos;

    private static DateTimeOffset? ObterDataPagamento(CompraPontos compra, InterPixCobranca cobranca) =>
        cobranca.Pagamentos.First(pagamento =>
            string.Equals(pagamento.Txid, compra.IdentificadorProvedor, StringComparison.Ordinal)
            && ValorCorresponde(pagamento.Valor, compra.PrecoCentavos)).Horario;

    private static CompraPontosResponse ToResponse(CompraPontos compra) => new(
        compra.Id,
        compra.PacotePontosId,
        compra.QuantidadePontos,
        compra.PrecoCentavos,
        compra.IdentificadorProvedor,
        compra.Status,
        compra.PixCopiaECola,
        compra.QrCodeBase64 is null ? null : $"data:image/png;base64,{compra.QrCodeBase64}",
        compra.CriadaEm,
        compra.ExpiraEm,
        compra.DataPagamento);
}

public sealed record CompraPontosResponse(
    long Id,
    int? PacotePontosId,
    int QuantidadePontos,
    int PrecoCentavos,
    string IdentificadorProvedor,
    string Status,
    string? PixCopiaECola,
    string? QrCodeDataUrl,
    DateTimeOffset CriadaEm,
    DateTimeOffset ExpiraEm,
    DateTimeOffset? DataPagamento);

public sealed class FalhaGerarPixException(long compraId, Exception innerException)
    : Exception("Não foi possível gerar a cobrança Pix no Inter.", innerException)
{
    public long CompraId { get; } = compraId;
}