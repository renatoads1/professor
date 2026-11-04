using System.Security.Claims;
using APILinguagem.Data;
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
}
