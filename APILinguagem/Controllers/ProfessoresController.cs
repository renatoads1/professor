using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using professor.Model;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ProfessoresController(
    AlunosDbContext dbContext,
    TurnstileVerifier turnstileVerifier) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Professores>>> GetAllAsync(CancellationToken cancellationToken)
    {
        var professores = await dbContext.Professores
            .AsNoTracking()
            .OrderBy(professor => professor.Nome)
            .ToListAsync(cancellationToken);

        return Ok(professores);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Professores>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var professor = await dbContext.Professores
            .AsNoTracking()
            .FirstOrDefaultAsync(professor => professor.Id == id, cancellationToken);

        return professor is null ? NotFound() : Ok(professor);
    }

    [HttpPost]
    public async Task<ActionResult<Professores>> CreateAsync(
        ProfessorCadastroRequest request,
        CancellationToken cancellationToken)
    {
        var captchaValido = await turnstileVerifier.VerifyAsync(
            request.TurnstileToken,
            "cadastro_professor",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!captchaValido)
        {
            return BadRequest(new { message = "A validação CAPTCHA falhou. Tente novamente." });
        }

        var emailEmUso = await dbContext.Professores
            .AnyAsync(professor => professor.Email == request.Professor.Email, cancellationToken);
        if (emailEmUso)
        {
            return Conflict(new { message = "Já existe um professor cadastrado com este e-mail." });
        }

        var professor = request.Professor;
        professor.Id = 0;
        dbContext.Professores.Add(professor);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetByIdAsync), new { id = professor.Id }, professor);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(
        int id,
        Professores professor,
        CancellationToken cancellationToken)
    {
        if (id != professor.Id)
        {
            return BadRequest();
        }

        var professorExistente = await dbContext.Professores.FindAsync([id], cancellationToken);
        if (professorExistente is null)
        {
            return NotFound();
        }

        var emailEmUso = await dbContext.Professores
            .AnyAsync(item => item.Id != id && item.Email == professor.Email, cancellationToken);
        if (emailEmUso)
        {
            return Conflict(new { message = "Já existe um professor cadastrado com este e-mail." });
        }

        professorExistente.Nome = professor.Nome;
        professorExistente.Email = professor.Email;
        professorExistente.Telefone = professor.Telefone;
        professorExistente.Pais = professor.Pais;
        professorExistente.Estado = professor.Estado;
        professorExistente.Cidade = professor.Cidade;
        professorExistente.LinguaDeEnsino = professor.LinguaDeEnsino;
        professorExistente.Apresentacao = professor.Apresentacao;
        professorExistente.Ativo = professor.Ativo;

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var professor = await dbContext.Professores.FindAsync([id], cancellationToken);
        if (professor is null)
        {
            return NotFound();
        }

        dbContext.Professores.Remove(professor);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
