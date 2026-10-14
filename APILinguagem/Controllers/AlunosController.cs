using APILinguagem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using professor.Model;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AlunosController(AlunosDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Alunos>>> GetAllAsync(CancellationToken cancellationToken)
    {
        var alunos = await dbContext.Alunos
            .AsNoTracking()
            .OrderBy(aluno => aluno.Id)
            .ToListAsync(cancellationToken);

        return Ok(alunos);
    }

    [ActionName(nameof(GetByIdAsync))]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Alunos>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var aluno = await dbContext.Alunos
            .AsNoTracking()
            .FirstOrDefaultAsync(aluno => aluno.Id == id, cancellationToken);

        return aluno is null ? NotFound() : Ok(aluno);
    }

    [HttpPost]
    public async Task<ActionResult<Alunos>> CreateAsync(Alunos aluno, CancellationToken cancellationToken)
    {
        dbContext.Alunos.Add(aluno);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetByIdAsync), new { id = aluno.Id }, aluno);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, Alunos aluno, CancellationToken cancellationToken)
    {
        if (id != aluno.Id)
        {
            return BadRequest();
        }

        var alunoExistente = await dbContext.Alunos.FindAsync([id], cancellationToken);
        if (alunoExistente is null)
        {
            return NotFound();
        }

        alunoExistente.Nome = aluno.Nome;
        alunoExistente.Email = aluno.Email;
        alunoExistente.Telefone = aluno.Telefone;

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var aluno = await dbContext.Alunos.FindAsync([id], cancellationToken);
        if (aluno is null)
        {
            return NotFound();
        }

        dbContext.Alunos.Remove(aluno);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
