using APILinguagem.Data;
using APILinguagem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/pacotes-pontos")]
[Authorize]
public sealed class PacotesPontosController(AlunosDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PacotePontosResponse>>> GetAtivosAsync(CancellationToken cancellationToken)
    {
        var pacotes = await dbContext.PacotesPontos
            .AsNoTracking()
            .Where(pacote => pacote.Ativo)
            .OrderBy(pacote => pacote.QuantidadePontos)
            .Select(pacote => new PacotePontosResponse(
                pacote.Id,
                pacote.Nome,
                pacote.QuantidadePontos,
                pacote.PrecoCentavos,
                pacote.Ativo))
            .ToListAsync(cancellationToken);

        return Ok(pacotes);
    }

    [HttpGet("gerenciar")]
    [Authorize(Policy = "Gerente")]
    public async Task<ActionResult<IReadOnlyList<PacotePontosResponse>>> GetTodosAsync(CancellationToken cancellationToken)
    {
        var pacotes = await dbContext.PacotesPontos
            .AsNoTracking()
            .OrderBy(pacote => pacote.QuantidadePontos)
            .Select(pacote => new PacotePontosResponse(
                pacote.Id,
                pacote.Nome,
                pacote.QuantidadePontos,
                pacote.PrecoCentavos,
                pacote.Ativo))
            .ToListAsync(cancellationToken);

        return Ok(pacotes);
    }

    [HttpGet("gerenciar/{id:int}", Name = nameof(GetGerenciadoAsync))]
    [Authorize(Policy = "Gerente")]
    public async Task<ActionResult<PacotePontosResponse>> GetGerenciadoAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var pacote = await dbContext.PacotesPontos
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (pacote is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(pacote));
    }

    [HttpPost("gerenciar")]
    [Authorize(Policy = "Gerente")]
    public async Task<ActionResult<PacotePontosResponse>> CriarAsync(
        PacotePontosRequest request,
        CancellationToken cancellationToken)
    {
        var nome = request.Nome.Trim();
        if (await dbContext.PacotesPontos.AnyAsync(pacote => pacote.Nome == nome, cancellationToken))
        {
            return Conflict(new { message = "Já existe um pacote com esse nome." });
        }

        var pacote = new PacotePontos(nome, request.QuantidadePontos, request.PrecoCentavos);
        dbContext.PacotesPontos.Add(pacote);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(pacote);
        return CreatedAtRoute(nameof(GetGerenciadoAsync), new { id = pacote.Id }, response);
    }

    [HttpPut("gerenciar/{id:int}")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> AtualizarAsync(
        int id,
        PacotePontosRequest request,
        CancellationToken cancellationToken)
    {
        var pacote = await dbContext.PacotesPontos.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (pacote is null)
        {
            return NotFound();
        }

        var nome = request.Nome.Trim();
        if (await dbContext.PacotesPontos.AnyAsync(item => item.Id != id && item.Nome == nome, cancellationToken))
        {
            return Conflict(new { message = "Já existe um pacote com esse nome." });
        }

        pacote.Atualizar(nome, request.QuantidadePontos, request.PrecoCentavos);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPatch("gerenciar/{id:int}/status")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> DefinirStatusAsync(
        int id,
        [FromBody] bool ativo,
        CancellationToken cancellationToken)
    {
        var pacote = await dbContext.PacotesPontos.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (pacote is null)
        {
            return NotFound();
        }

        if (ativo)
        {
            pacote.Ativar();
        }
        else
        {
            pacote.Desativar();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static PacotePontosResponse ToResponse(PacotePontos pacote) => new(
        pacote.Id,
        pacote.Nome,
        pacote.QuantidadePontos,
        pacote.PrecoCentavos,
        pacote.Ativo);
}

public sealed record PacotePontosResponse(
    int Id,
    string Nome,
    int QuantidadePontos,
    int PrecoCentavos,
    bool Ativo);
