using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using professor.Model;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AlunosController(AlunosDbContext dbContext, TurnstileVerifier turnstileVerifier) : ControllerBase
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
    public async Task<ActionResult<Alunos>> CreateAsync(AlunoCadastroRequest request, CancellationToken cancellationToken)
    {
        var captchaValido = await turnstileVerifier.VerifyAsync(
            request.TurnstileToken,
            "cadastro_aluno",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!captchaValido)
        {
            return BadRequest(new { message = "A validação CAPTCHA falhou. Tente novamente." });
        }

        var aluno = request.Aluno;
        aluno.Id = 0;
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
        alunoExistente.Pais = aluno.Pais;
        alunoExistente.Estado = aluno.Estado;
        alunoExistente.Cidade = aluno.Cidade;
        alunoExistente.LinguaDeInteresse = aluno.LinguaDeInteresse;
        alunoExistente.Estudando = aluno.Estudando;

        //await dbContext.SaveChangesAsync(cancellationToken);
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
