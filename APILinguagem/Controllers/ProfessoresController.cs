using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using professor.Model;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ProfessoresController(
    AlunosDbContext dbContext,
    ContaAcessoService contaAcessoService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<Professores>>> GetAllAsync(CancellationToken cancellationToken)
    {
        var professores = await dbContext.Professores
            .AsNoTracking()
            .OrderBy(professor => professor.Nome)
            .ToListAsync(cancellationToken);

        return Ok(professores);
    }

    [HttpGet("{id:int}", Name = "GetProfessorById")]
    [Authorize]
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
        if (!request.Professor.TermosAceitos)
        {
            return BadRequest(new { message = "É necessário aceitar os termos para cadastrar o professor." });
        }

        // CAPTCHA temporariamente desativado no cadastro de professores.

        var emailEmUso = await dbContext.Professores
            .AnyAsync(professor => professor.Email == request.Professor.Email, cancellationToken);
        var emailAlunoEmUso = await dbContext.Alunos
            .AnyAsync(aluno => aluno.Email.ToUpper() == ContaAcessoService.NormalizarEmail(request.Professor.Email), cancellationToken);
        var contaExistente = await dbContext.ContasAcesso
            .AnyAsync(conta => conta.EmailNormalizado == ContaAcessoService.NormalizarEmail(request.Professor.Email), cancellationToken);
        if (emailEmUso || emailAlunoEmUso || contaExistente)
        {
            return Conflict(new { message = "Já existe um professor cadastrado com este e-mail." });
        }

        var professor = request.Professor;
        professor.Id = 0;
        dbContext.Professores.Add(professor);
        await dbContext.SaveChangesAsync(cancellationToken);
        await contaAcessoService.DefinirSenhaAsync(professor.Email, professor.Nome, "professor", professor.Id, request.Senha, cancellationToken);

        return CreatedAtRoute("GetProfessorById", new { id = professor.Id }, professor);
    }

    [HttpPut("{id:int}")]
    [Authorize]
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
        professorExistente.HoraAula = professor.HoraAula;
        professorExistente.Apresentacao = professor.Apresentacao;
        professorExistente.Ativo = professor.Ativo;

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize]
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
