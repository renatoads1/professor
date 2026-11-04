using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using professor.Model;
using System.Security.Claims;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ProfessoresController(
    AlunosDbContext dbContext,
    ContaAcessoService contaAcessoService,
    PesquisaPontosService pesquisaPontosService) : ControllerBase
{
    [HttpPost("pesquisar")]
    [Authorize]
    public async Task<ActionResult<PesquisaResponse<Professores>>> PesquisarAsync(
        PesquisaProfessoresRequest request,
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

        var cobranca = await pesquisaPontosService.ExecutarAsync<Professores>(
            contaAcessoId,
            chaveIdempotencia,
            impressaoPesquisa,
            async token =>
            {
            var consulta = dbContext.Professores.AsNoTracking().AsQueryable();
            if (int.TryParse(request.Id, out var id))
            {
                consulta = consulta.Where(professor => professor.Id == id);
            }

            if (!string.IsNullOrWhiteSpace(request.Id) && !int.TryParse(request.Id, out _))
            {
                consulta = consulta.Where(_ => false);
            }

            if (!string.IsNullOrWhiteSpace(request.Nome))
            {
                consulta = consulta.Where(professor => EF.Functions.ILike(professor.Nome, $"%{request.Nome.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                consulta = consulta.Where(professor => EF.Functions.ILike(professor.Email, $"%{request.Email.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Telefone))
            {
                consulta = consulta.Where(professor => EF.Functions.ILike(professor.Telefone, $"%{request.Telefone.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Pais))
            {
                consulta = consulta.Where(professor => EF.Functions.ILike(professor.Pais, $"%{request.Pais.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Estado))
            {
                consulta = consulta.Where(professor => EF.Functions.ILike(professor.Estado, $"%{request.Estado.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Cidade))
            {
                consulta = consulta.Where(professor => EF.Functions.ILike(professor.Cidade, $"%{request.Cidade.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.LinguaDeEnsino))
            {
                consulta = consulta.Where(professor => EF.Functions.ILike(professor.LinguaDeEnsino, $"%{request.LinguaDeEnsino.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(request.Apresentacao))
            {
                consulta = consulta.Where(professor => EF.Functions.ILike(professor.Apresentacao, $"%{request.Apresentacao.Trim()}%"));
            }

            if (request.PrecoMinimo.HasValue)
            {
                consulta = consulta.Where(professor => professor.HoraAula >= request.PrecoMinimo.Value);
            }

            if (request.PrecoMaximo.HasValue)
            {
                consulta = consulta.Where(professor => professor.HoraAula <= request.PrecoMaximo.Value);
            }

            if (request.Ativo.HasValue)
            {
                consulta = consulta.Where(professor => professor.Ativo == request.Ativo.Value);
            }

            return (IReadOnlyList<Professores>)await consulta
                .OrderBy(professor => professor.Nome)
                .ToListAsync(token);
            }, cancellationToken);

        return cobranca.Status switch
        {
            PesquisaTransacaoStatus.Concluida =>
                Ok(new PesquisaResponse<Professores>(cobranca.Resultados, cobranca.SaldoPontos)),
            PesquisaTransacaoStatus.SaldoInsuficiente =>
                StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Saldo insuficiente para realizar a pesquisa.", cobranca.SaldoPontos }),
            PesquisaTransacaoStatus.ContaInexistente => NotFound(),
            PesquisaTransacaoStatus.ChaveReutilizada => Conflict(new { message = "A chave de idempotência já foi usada em outra pesquisa." }),
            _ => BadRequest(new { message = "A chave de idempotência é inválida." })
        };
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

    [HttpGet("{id:int}/foto")]
    [Authorize]
    public async Task<IActionResult> GetFotoAsync(int id, CancellationToken cancellationToken)
    {
        var foto = await dbContext.Professores
            .AsNoTracking()
            .Where(professor => professor.Id == id)
            .Select(professor => new { professor.FotoPerfil, professor.FotoPerfilContentType })
            .SingleOrDefaultAsync(cancellationToken);

        if (foto?.FotoPerfil is null || string.IsNullOrWhiteSpace(foto.FotoPerfilContentType))
        {
            return NotFound();
        }

        return File(foto.FotoPerfil, foto.FotoPerfilContentType);
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

        if (!ProfilePhotoValidator.TryValidate(request.FotoPerfil, request.FotoPerfilContentType, out var fotoError))
        {
            return BadRequest(new { message = fotoError });
        }

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
        professor.FotoPerfil = request.FotoPerfil;
        professor.FotoPerfilContentType = request.FotoPerfilContentType;
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
