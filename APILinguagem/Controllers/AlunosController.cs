using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using professor.Model;
using System.Security.Claims;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AlunosController(
    AlunosDbContext dbContext,
    ContaAcessoService contaAcessoService,
    PesquisaPontosService pesquisaPontosService) : ControllerBase
{
    [HttpPost("pesquisar")]
    [Authorize]
    public async Task<ActionResult<PesquisaResponse<Alunos>>> PesquisarAsync(
        PesquisaAlunosRequest request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var contaAcessoId)
            || request.ChaveIdempotencia == Guid.Empty)
        {
            return BadRequest(new { message = "A busca não possui uma identidade ou chave de idempotência válida." });
        }

        var chaveIdempotencia = request.ChaveIdempotencia.ToString("N");
        var impressaoPesquisa = PesquisaPontosService.CriarImpressaoPesquisa(
            request with { ChaveIdempotencia = Guid.Empty });

        var cobranca = await pesquisaPontosService.ExecutarAsync<Alunos>(
            contaAcessoId,
            chaveIdempotencia,
            impressaoPesquisa,
            async token =>
            {
            var consulta = dbContext.Alunos.AsNoTracking().AsQueryable();
            if (int.TryParse(request.Id, out var id))
            {
                consulta = consulta.Where(aluno => aluno.Id == id);
            }

            if (!string.IsNullOrWhiteSpace(request.Id) && !int.TryParse(request.Id, out _))
            {
                consulta = consulta.Where(_ => false);
            }

            if (!string.IsNullOrWhiteSpace(request.Nome))
            {
                consulta = consulta.Where(aluno => EF.Functions.ILike(aluno.Nome, $"%{request.Nome.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                consulta = consulta.Where(aluno => EF.Functions.ILike(aluno.Email, $"%{request.Email.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Telefone))
            {
                consulta = consulta.Where(aluno => EF.Functions.ILike(aluno.Telefone, $"%{request.Telefone.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Pais))
            {
                consulta = consulta.Where(aluno => EF.Functions.ILike(aluno.Pais, $"%{request.Pais.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Estado))
            {
                consulta = consulta.Where(aluno => EF.Functions.ILike(aluno.Estado, $"%{request.Estado.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Cidade))
            {
                consulta = consulta.Where(aluno => EF.Functions.ILike(aluno.Cidade, $"%{request.Cidade.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.LinguaDeInteresse))
            {
                consulta = consulta.Where(aluno => EF.Functions.ILike(aluno.LinguaDeInteresse, $"%{request.LinguaDeInteresse.Trim()}%"));
            }

            if (request.Estudando.HasValue)
            {
                consulta = consulta.Where(aluno => aluno.Estudando == request.Estudando.Value);
            }

            return (IReadOnlyList<Alunos>)await consulta
                .OrderBy(aluno => aluno.Id)
                .ToListAsync(token);
            }, cancellationToken);

        return cobranca.Status switch
        {
            PesquisaTransacaoStatus.Concluida =>
                Ok(new PesquisaResponse<Alunos>(cobranca.Resultados, cobranca.SaldoPontos)),
            PesquisaTransacaoStatus.SaldoInsuficiente =>
                StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Saldo insuficiente para realizar a pesquisa.", cobranca.SaldoPontos }),
            PesquisaTransacaoStatus.ContaInexistente => NotFound(),
            PesquisaTransacaoStatus.ChaveReutilizada => Conflict(new { message = "A chave de idempotência já foi usada em outra pesquisa." }),
            _ => BadRequest(new { message = "A chave de idempotência é inválida." })
        };
    }

    [ActionName(nameof(GetByIdAsync))]
    [HttpGet("{id:int}")]
    [Authorize]
    public async Task<ActionResult<Alunos>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var aluno = await dbContext.Alunos
            .AsNoTracking()
            .FirstOrDefaultAsync(aluno => aluno.Id == id, cancellationToken);

        return aluno is null ? NotFound() : Ok(aluno);
    }

    [HttpGet("{id:int}/foto")]
    [Authorize]
    public async Task<IActionResult> GetFotoAsync(int id, CancellationToken cancellationToken)
    {
        var foto = await dbContext.Alunos
            .AsNoTracking()
            .Where(aluno => aluno.Id == id)
            .Select(aluno => new { aluno.FotoPerfil, aluno.FotoPerfilContentType })
            .SingleOrDefaultAsync(cancellationToken);

        if (foto?.FotoPerfil is null || string.IsNullOrWhiteSpace(foto.FotoPerfilContentType))
        {
            return NotFound();
        }

        return File(foto.FotoPerfil, foto.FotoPerfilContentType);
    }

    [HttpPost]
    public async Task<ActionResult<Alunos>> CreateAsync(AlunoCadastroRequest request, CancellationToken cancellationToken)
    {
        if (!request.Aluno.TermosAceitos)
        {
            return BadRequest(new { message = "É necessário aceitar os termos para cadastrar o aluno." });
        }

        if (!ProfilePhotoValidator.TryValidate(request.FotoPerfil, request.FotoPerfilContentType, out var fotoError))
        {
            return BadRequest(new { message = fotoError });
        }

        var aluno = request.Aluno;
        aluno.FotoPerfil = request.FotoPerfil;
        aluno.FotoPerfilContentType = request.FotoPerfilContentType;
        var emailNormalizado = ContaAcessoService.NormalizarEmail(aluno.Email);
        if (await dbContext.ContasAcesso.AnyAsync(conta => conta.EmailNormalizado == emailNormalizado, cancellationToken)
            || await dbContext.Professores.AnyAsync(professor => professor.Email.ToUpper() == emailNormalizado, cancellationToken))
        {
            return Conflict(new { message = "Já existe uma conta cadastrada com este e-mail." });
        }

        aluno.Id = 0;
        dbContext.Alunos.Add(aluno);
        await dbContext.SaveChangesAsync(cancellationToken);
        await contaAcessoService.DefinirSenhaAsync(aluno.Email, aluno.Nome, "aluno", aluno.Id, request.Senha, cancellationToken);
        return CreatedAtAction(nameof(GetByIdAsync), new { id = aluno.Id }, aluno);
    }

    [HttpPut("{id:int}")]
    [Authorize]
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

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize]
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
