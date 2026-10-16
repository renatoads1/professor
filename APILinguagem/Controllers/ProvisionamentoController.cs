using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ProvisionamentoController(
    AlunosDbContext dbContext,
    ContaAcessoService contaAcessoService,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("senha-inicial")]
    public async Task<IActionResult> DefinirSenhaInicialAsync(
        DefinirSenhaInicialRequest request,
        CancellationToken cancellationToken)
    {
        var chaveConfigurada = configuration["Authentication:AdminSetupKey"];
        if (string.IsNullOrWhiteSpace(chaveConfigurada))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "O provisionamento administrativo não está configurado." });
        }

        var configurada = System.Text.Encoding.UTF8.GetBytes(chaveConfigurada);
        var fornecida = System.Text.Encoding.UTF8.GetBytes(request.ChaveAdministrativa);
        if (configurada.Length != fornecida.Length
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(configurada, fornecida))
        {
            return Unauthorized();
        }

        var emailNormalizado = ContaAcessoService.NormalizarEmail(request.Email);
        var aluno = await dbContext.Alunos.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Email.ToUpper() == emailNormalizado, cancellationToken);
        if (aluno is not null)
        {
            await contaAcessoService.DefinirSenhaAsync(aluno.Email, aluno.Nome, "aluno", aluno.Id, request.Senha, cancellationToken);
            return NoContent();
        }

        var professor = await dbContext.Professores.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Email.ToUpper() == emailNormalizado, cancellationToken);
        if (professor is not null)
        {
            await contaAcessoService.DefinirSenhaAsync(professor.Email, professor.Nome, "professor", professor.Id, request.Senha, cancellationToken);
            return NoContent();
        }

        return NotFound(new { message = "Não existe perfil cadastrado com este e-mail." });
    }
}
