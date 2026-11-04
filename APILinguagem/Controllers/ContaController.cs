using System.Security.Claims;
using APILinguagem.Data;
using APILinguagem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ContaController(AlunosDbContext dbContext) : ControllerBase
{
    [HttpGet("saldo")]
    public async Task<IActionResult> GetSaldoAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var contaId))
        {
            return Unauthorized();
        }

        var saldoPontos = await dbContext.ContasAcesso
            .AsNoTracking()
            .Where(conta => conta.Id == contaId)
            .Select(conta => (int?)conta.SaldoPontos)
            .SingleOrDefaultAsync(cancellationToken);

        return saldoPontos is null
            ? NotFound()
            : Ok(new { saldoPontos = saldoPontos.Value });
    }

    [HttpGet("lancamentos")]
    public async Task<IActionResult> GetLancamentosAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var contaId))
        {
            return Unauthorized();
        }

        var lancamentos = await dbContext.LancamentosPontos
            .AsNoTracking()
            .Where(lancamento => lancamento.ContaAcessoId == contaId)
            .OrderByDescending(lancamento => lancamento.DataLancamento)
            .ThenByDescending(lancamento => lancamento.Id)
            .Take(100)
            .Select(lancamento => new LancamentoPontosResponse(
                lancamento.Id,
                lancamento.Quantidade,
                lancamento.Motivo,
                lancamento.DataLancamento))
            .ToListAsync(cancellationToken);

        return Ok(lancamentos);
    }
}

public sealed record LancamentoPontosResponse(
    long Id,
    int Quantidade,
    string Motivo,
    DateTimeOffset DataLancamento);
