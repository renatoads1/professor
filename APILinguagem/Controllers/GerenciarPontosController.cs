using System.Security.Claims;
using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/gerenciar-pontos")]
[Authorize(Policy = "Gerente")]
public sealed class GerenciarPontosController(AlunosDbContext dbContext) : ControllerBase
{
    private const string ChavePrefixo = "gerente-ajuste:";
    private const string MotivoPrefixo = "Ajuste gerente: ";

    [HttpGet("contas")]
    public async Task<ActionResult<IReadOnlyList<ContaPontosResponse>>> BuscarContasAsync(
        [FromQuery] string email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length < 3)
        {
            return BadRequest(new { message = "Informe ao menos 3 caracteres do e-mail." });
        }

        var termo = ContaAcessoService.NormalizarEmail(email);
        var contas = await dbContext.ContasAcesso
            .AsNoTracking()
            .Where(conta => (conta.TipoPerfil == "aluno" || conta.TipoPerfil == "professor")
                && conta.EmailNormalizado.Contains(termo))
            .OrderBy(conta => conta.EmailNormalizado)
            .Take(20)
            .Select(conta => new ContaPontosResponse(
                conta.Id, conta.Email, conta.Nome, conta.TipoPerfil, conta.SaldoPontos))
            .ToListAsync(cancellationToken);

        return Ok(contas);
    }

    [HttpGet("contas/{contaId:int}/ajustes")]
    public async Task<ActionResult<IReadOnlyList<AjustePontosResponse>>> ListarAjustesAsync(
        int contaId,
        CancellationToken cancellationToken)
    {
        var existeConta = await dbContext.ContasAcesso.AsNoTracking()
            .AnyAsync(conta => conta.Id == contaId
                && (conta.TipoPerfil == "aluno" || conta.TipoPerfil == "professor"), cancellationToken);
        if (!existeConta)
        {
            return NotFound();
        }

        var lancamentos = await dbContext.LancamentosPontos
            .AsNoTracking()
            .Where(lancamento => lancamento.ContaAcessoId == contaId
                && lancamento.ChaveIdempotencia.StartsWith(ChavePrefixo))
            .OrderBy(lancamento => lancamento.DataLancamento)
            .ThenBy(lancamento => lancamento.Id)
            .Select(lancamento => new { lancamento.Id, lancamento.ChaveIdempotencia, lancamento.Quantidade, lancamento.Motivo, lancamento.DataLancamento })
            .ToListAsync(cancellationToken);

        var ajustes = lancamentos
            .Select(lancamento => (Lancamento: lancamento, Id: ObterAjusteId(lancamento.ChaveIdempotencia)))
            .Where(item => item.Id.HasValue)
            .GroupBy(item => item.Id!.Value)
            .Select(grupo =>
            {
                var lancamentoMaisRecente = grupo.OrderByDescending(item => item.Lancamento.DataLancamento)
                    .ThenByDescending(item => item.Lancamento.Id).First().Lancamento;
                return new AjustePontosResponse(
                    grupo.Key,
                    checked((int)grupo.Sum(item => (long)item.Lancamento.Quantidade)),
                    ObterMotivoExibicao(lancamentoMaisRecente.Motivo),
                    lancamentoMaisRecente.DataLancamento);
            })
            .Where(ajuste => ajuste.Quantidade != 0)
            .OrderByDescending(ajuste => ajuste.Data)
            .ToList();

        return Ok(ajustes);
    }

    [HttpPost("contas/{contaId:int}/ajustes")]
    public async Task<IActionResult> CriarAjusteAsync(
        int contaId,
        AjustePontosRequest request,
        CancellationToken cancellationToken)
    {
        if (!ValidarRequest(request, out var motivoErro))
        {
            return BadRequest(new { message = motivoErro });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var conta = await ObterContaGerenciavelAsync(contaId, cancellationToken);
        if (conta is null)
        {
            return NotFound();
        }

        var aplicacao = AplicarVariacao(conta, request.Quantidade, request.Motivo, Guid.NewGuid(), await ObterProximoNumeroAsync(contaId, null, cancellationToken));
        if (aplicacao is not null)
        {
            return Conflict(new { message = aplicacao });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("contas/{contaId:int}/ajustes/{ajusteId:guid}")]
    public async Task<IActionResult> EditarAjusteAsync(
        int contaId,
        Guid ajusteId,
        AjustePontosRequest request,
        CancellationToken cancellationToken)
    {
        if (!ValidarRequest(request, out var motivoErro))
        {
            return BadRequest(new { message = motivoErro });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var conta = await ObterContaGerenciavelAsync(contaId, cancellationToken);
        if (conta is null)
        {
            return NotFound();
        }

        var lancamentos = await ObterLancamentosAjusteAsync(contaId, ajusteId, cancellationToken);
        if (lancamentos.Count == 0)
        {
            return NotFound();
        }

        var quantidadeAtual = checked((int)lancamentos.Sum(lancamento => (long)lancamento.Quantidade));
        if (quantidadeAtual == 0)
        {
            return NotFound();
        }

        var diferenca = (long)request.Quantidade - quantidadeAtual;
        if (diferenca is < int.MinValue or > int.MaxValue)
        {
            return BadRequest(new { message = "A alteração excede o limite permitido." });
        }

        var proximoNumero = await ObterProximoNumeroAsync(contaId, ajusteId, cancellationToken);
        if (diferenca != 0)
        {
            var aplicacao = AplicarVariacao(conta, (int)diferenca, request.Motivo, ajusteId,
                proximoNumero);
            if (aplicacao is not null)
            {
                return Conflict(new { message = aplicacao });
            }
        }
        else if (!string.Equals(ObterMotivoExibicao(lancamentos.OrderByDescending(lancamento => lancamento.DataLancamento)
            .ThenByDescending(lancamento => lancamento.Id).First().Motivo), request.Motivo.Trim(), StringComparison.Ordinal))
        {
            // Registra a troca do motivo sem alterar o saldo, mantendo os ajustes auditáveis.
            AtualizarMotivoAjuste(conta, contaId, ajusteId, request.Motivo, proximoNumero);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("contas/{contaId:int}/ajustes/{ajusteId:guid}")]
    public async Task<IActionResult> ExcluirAjusteAsync(
        int contaId,
        Guid ajusteId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var conta = await ObterContaGerenciavelAsync(contaId, cancellationToken);
        if (conta is null)
        {
            return NotFound();
        }

        var lancamentos = await ObterLancamentosAjusteAsync(contaId, ajusteId, cancellationToken);
        if (lancamentos.Count == 0)
        {
            return NotFound();
        }

        var quantidadeAtual = checked((int)lancamentos.Sum(lancamento => (long)lancamento.Quantidade));
        if (quantidadeAtual == 0)
        {
            return NotFound();
        }

        var aplicacao = AplicarVariacao(conta, -quantidadeAtual, "Estorno de ajuste gerencial", ajusteId,
            await ObterProximoNumeroAsync(contaId, ajusteId, cancellationToken));
        if (aplicacao is not null)
        {
            return Conflict(new { message = aplicacao });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ContaAcesso?> ObterContaGerenciavelAsync(int contaId, CancellationToken cancellationToken) =>
        await dbContext.ContasAcesso.SingleOrDefaultAsync(conta => conta.Id == contaId
            && (conta.TipoPerfil == "aluno" || conta.TipoPerfil == "professor"), cancellationToken);

    private async Task<List<LancamentoPontos>> ObterLancamentosAjusteAsync(
        int contaId,
        Guid ajusteId,
        CancellationToken cancellationToken)
    {
        var chavePrefixo = $"{ChavePrefixo}{ajusteId:N}:";
        return await dbContext.LancamentosPontos
            .Where(lancamento => lancamento.ContaAcessoId == contaId
                && lancamento.ChaveIdempotencia.StartsWith(chavePrefixo))
            .ToListAsync(cancellationToken);
    }

    private async Task<int> ObterProximoNumeroAsync(int contaId, Guid? ajusteId, CancellationToken cancellationToken)
    {
        var chavePrefixo = ajusteId is null
            ? ChavePrefixo
            : $"{ChavePrefixo}{ajusteId.Value:N}:";
        var quantidade = await dbContext.LancamentosPontos.CountAsync(lancamento =>
            lancamento.ContaAcessoId == contaId
            && lancamento.ChaveIdempotencia.StartsWith(chavePrefixo), cancellationToken);
        return quantidade + 1;
    }

    private string? AplicarVariacao(ContaAcesso conta, int quantidade, string motivo, Guid ajusteId, int numero)
    {
        var novoSaldo = (long)conta.SaldoPontos + quantidade;
        if (novoSaldo < 0)
        {
            return "O ajuste deixaria o saldo do usuário negativo.";
        }
        if (novoSaldo > int.MaxValue)
        {
            return "O ajuste excede o limite máximo do saldo.";
        }

        conta.SaldoPontos = (int)novoSaldo;
        var gerenteId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "desconhecido";
        var motivoAuditoria = $"{MotivoPrefixo}{motivo.Trim()} (gerente {gerenteId})";
        if (motivoAuditoria.Length > 200)
        {
            motivoAuditoria = motivoAuditoria[..200];
        }

        dbContext.LancamentosPontos.Add(new LancamentoPontos(
            conta.Id,
            quantidade,
            motivoAuditoria,
            $"{ChavePrefixo}{ajusteId:N}:{numero}"));
        return null;
    }

    private string? AtualizarMotivoAjuste(ContaAcesso conta, int contaId, Guid ajusteId, string motivo, int numero)
    {
        var gerenteId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "desconhecido";
        var motivoAuditoria = $"{MotivoPrefixo}{motivo.Trim()} (gerente {gerenteId})";
        if (motivoAuditoria.Length > 200)
        {
            motivoAuditoria = motivoAuditoria[..200];
        }

        dbContext.LancamentosPontos.Add(new LancamentoPontos(
            contaId,
            conta.SaldoPontos == int.MaxValue ? -1 : 1,
            motivoAuditoria,
            $"{ChavePrefixo}{ajusteId:N}:{numero}"));
        dbContext.LancamentosPontos.Add(new LancamentoPontos(
            contaId,
            conta.SaldoPontos == int.MaxValue ? 1 : -1,
            motivoAuditoria,
            $"{ChavePrefixo}{ajusteId:N}:{numero + 1}"));
        return null;
    }

    private static bool ValidarRequest(AjustePontosRequest request, out string? erro)
    {
        if (request.Quantidade == 0)
        {
            erro = "A quantidade de pontos não pode ser zero.";
            return false;
        }

        if (request.Quantidade == int.MinValue)
        {
            erro = "A quantidade está fora do limite permitido.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.Motivo) || request.Motivo.Trim().Length > 150)
        {
            erro = "Informe um motivo com até 150 caracteres.";
            return false;
        }

        erro = null;
        return true;
    }

    private static Guid? ObterAjusteId(string chave)
    {
        var partes = chave.Split(':');
        return partes.Length == 3
            && partes[0] == "gerente-ajuste"
            && Guid.TryParseExact(partes[1], "N", out var id)
                ? id
                : null;
    }

    private static string ObterMotivoExibicao(string motivo)
    {
        if (!motivo.StartsWith(MotivoPrefixo, StringComparison.Ordinal))
        {
            return motivo;
        }

        var inicio = MotivoPrefixo.Length;
        var fimAuditoria = motivo.LastIndexOf(" (gerente ", StringComparison.Ordinal);
        return fimAuditoria > inicio ? motivo[inicio..fimAuditoria] : motivo[inicio..];
    }

    public sealed record AjustePontosRequest(int Quantidade, string Motivo);
    public sealed record ContaPontosResponse(int Id, string Email, string Nome, string TipoPerfil, int SaldoPontos);
    public sealed record AjustePontosResponse(Guid Id, int Quantidade, string Motivo, DateTimeOffset Data);
}
