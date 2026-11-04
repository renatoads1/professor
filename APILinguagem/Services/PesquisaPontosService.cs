using System.Security.Cryptography;
using System.Text.Json;
using APILinguagem.Data;
using APILinguagem.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace APILinguagem.Services;

public sealed class PesquisaPontosService(AlunosDbContext dbContext)
{
    public const int CustoPesquisa = 1;

    public static string CriarImpressaoPesquisa(object request)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, request.GetType());
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    public async Task<PesquisaTransacaoResult<T>> ExecutarAsync<T>(
        int contaAcessoId,
        string chaveIdempotencia,
        string impressaoPesquisa,
        Func<CancellationToken, Task<IReadOnlyList<T>>> pesquisarAsync,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chaveIdempotencia) || chaveIdempotencia.Length > 100
            || string.IsNullOrWhiteSpace(impressaoPesquisa) || impressaoPesquisa.Length != 64)
        {
            return new(PesquisaTransacaoStatus.ChaveInvalida, [], 0);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var conta = await dbContext.ContasAcesso
            .FromSqlInterpolated($"SELECT * FROM \"ContasAcesso\" WHERE \"Id\" = {contaAcessoId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (conta is null)
        {
            return new(PesquisaTransacaoStatus.ContaInexistente, [], 0);
        }

        var lancamentoExistente = await dbContext.LancamentosPontos
            .AsNoTracking()
            .SingleOrDefaultAsync(lancamento =>
                lancamento.ContaAcessoId == contaAcessoId
                && lancamento.ChaveIdempotencia == chaveIdempotencia, cancellationToken);

        if (lancamentoExistente is not null)
        {
            if (lancamentoExistente.Motivo != $"Pesquisa:{impressaoPesquisa}")
            {
                return new(PesquisaTransacaoStatus.ChaveReutilizada, [], conta.SaldoPontos);
            }

            var retryResultados = await pesquisarAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(PesquisaTransacaoStatus.Concluida, retryResultados, conta.SaldoPontos);
        }

        if (conta.SaldoPontos < CustoPesquisa)
        {
            return new(PesquisaTransacaoStatus.SaldoInsuficiente, [], conta.SaldoPontos);
        }

        var resultados = await pesquisarAsync(cancellationToken);
        if (resultados.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(PesquisaTransacaoStatus.Concluida, resultados, conta.SaldoPontos);
        }

        conta.SaldoPontos -= CustoPesquisa;
        dbContext.LancamentosPontos.Add(new LancamentoPontos(
            contaAcessoId,
            -CustoPesquisa,
            $"Pesquisa:{impressaoPesquisa}",
            chaveIdempotencia));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            var lancamentoConcorrente = await dbContext.LancamentosPontos
                .AsNoTracking()
                .SingleOrDefaultAsync(lancamento =>
                    lancamento.ContaAcessoId == contaAcessoId
                    && lancamento.ChaveIdempotencia == chaveIdempotencia, cancellationToken);
            if (lancamentoConcorrente is null || lancamentoConcorrente.Motivo != $"Pesquisa:{impressaoPesquisa}")
            {
                return new(PesquisaTransacaoStatus.ChaveReutilizada, [], await ObterSaldoAsync(contaAcessoId, cancellationToken));
            }

            var saldoRetry = await ObterSaldoAsync(contaAcessoId, cancellationToken);
            var retryResultados = await pesquisarAsync(cancellationToken);
            return new(PesquisaTransacaoStatus.Concluida, retryResultados, saldoRetry);
        }

        return new(PesquisaTransacaoStatus.Concluida, resultados, conta.SaldoPontos);
    }

    private Task<int> ObterSaldoAsync(int contaAcessoId, CancellationToken cancellationToken) => dbContext.ContasAcesso
        .Where(conta => conta.Id == contaAcessoId)
        .Select(conta => conta.SaldoPontos)
        .SingleAsync(cancellationToken);
}

public enum PesquisaTransacaoStatus
{
    Concluida,
    ChaveReutilizada,
    SaldoInsuficiente,
    ContaInexistente,
    ChaveInvalida
}

public sealed record PesquisaTransacaoResult<T>(PesquisaTransacaoStatus Status, IReadOnlyList<T> Resultados, int SaldoPontos);
