using System.Security.Claims;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/agenda")]
[Authorize]
public sealed class AgendaController(AgendaAulasService agendaAulasService) : ControllerBase
{
    [HttpGet("minha-disponibilidade")]
    public async Task<ActionResult<DisponibilidadeSemanalResponse>> ObterMinhaDisponibilidadeSemanalAsync(
        CancellationToken cancellationToken)
    {
        if (!TryObterContaAcessoId(out var contaAcessoId))
        {
            return Unauthorized();
        }

        var resultado = await agendaAulasService.ObterMinhaDisponibilidadeSemanalAsync(contaAcessoId, cancellationToken);
        return resultado.Status switch
        {
            AgendaOperacaoStatus.Concluida => Ok(resultado.Valor),
            AgendaOperacaoStatus.ContaNaoEncontrada or AgendaOperacaoStatus.ProfessorNaoEncontrado => NotFound(),
            AgendaOperacaoStatus.PerfilIncorreto => Forbid(),
            _ => BadRequest()
        };
    }

    [HttpGet("minha-agenda")]
    public async Task<ActionResult<IReadOnlyList<HorarioAgendaResponse>>> ListarMinhaAgendaAsync(
        [FromQuery] DateTimeOffset? inicioUtc,
        [FromQuery] DateTimeOffset? fimUtc,
        CancellationToken cancellationToken)
    {
        if (!TryObterContaAcessoId(out var contaAcessoId))
        {
            return Unauthorized();
        }

        if (!inicioUtc.HasValue || !fimUtc.HasValue)
        {
            return BadRequest(new { message = "Informe o início e o fim do período em UTC." });
        }

        var resultado = await agendaAulasService.ListarMinhaAgendaAsync(
            contaAcessoId, inicioUtc.Value, fimUtc.Value, cancellationToken);
        return resultado.Status switch
        {
            AgendaOperacaoStatus.Concluida => Ok(resultado.Valor),
            AgendaOperacaoStatus.ContaNaoEncontrada or AgendaOperacaoStatus.ProfessorNaoEncontrado => NotFound(),
            AgendaOperacaoStatus.PerfilIncorreto => Forbid(),
            AgendaOperacaoStatus.TarifaInvalida => Conflict(new { message = "O preço da hora-aula deve ser um valor positivo em reais inteiros para converter em pontos." }),
            AgendaOperacaoStatus.FusoHorarioInvalido => Conflict(new { message = "O fuso horário da agenda do professor está inválido." }),
            _ => BadRequest(new { message = "O período solicitado é inválido ou excede 31 dias." })
        };
    }

    [HttpPut("disponibilidade-semanal")]
    public async Task<IActionResult> DefinirDisponibilidadeSemanalAsync(
        AtualizarDisponibilidadeSemanalRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryObterContaAcessoId(out var contaAcessoId))
        {
            return Unauthorized();
        }

        var resultado = await agendaAulasService.DefinirDisponibilidadeAsync(contaAcessoId, request, cancellationToken);
        return resultado.Status switch
        {
            AgendaOperacaoStatus.Concluida => NoContent(),
            AgendaOperacaoStatus.ContaNaoEncontrada or AgendaOperacaoStatus.ProfessorNaoEncontrado => NotFound(),
            AgendaOperacaoStatus.PerfilIncorreto => Forbid(),
            AgendaOperacaoStatus.ProfessorInativo => Conflict(new { message = "O perfil do professor está inativo." }),
            AgendaOperacaoStatus.FusoHorarioInvalido => BadRequest(new { message = "Informe um fuso horário válido." }),
            _ => BadRequest(new { message = "A grade semanal contém dados inválidos ou horários sobrepostos." })
        };
    }

    [HttpDelete("disponibilidade-semanal")]
    public async Task<IActionResult> LimparDisponibilidadeSemanalAsync(CancellationToken cancellationToken)
    {
        if (!TryObterContaAcessoId(out var contaAcessoId))
        {
            return Unauthorized();
        }

        var resultado = await agendaAulasService.LimparDisponibilidadeAsync(contaAcessoId, cancellationToken);
        return resultado switch
        {
            AgendaOperacaoStatus.Concluida => NoContent(),
            AgendaOperacaoStatus.ContaNaoEncontrada or AgendaOperacaoStatus.ProfessorNaoEncontrado => NotFound(),
            AgendaOperacaoStatus.PerfilIncorreto => Forbid(),
            _ => BadRequest()
        };
    }

    [HttpGet("professores/{professorId:int}/horarios")]
    public async Task<ActionResult<IReadOnlyList<HorarioDisponivelResponse>>> ListarHorariosDisponiveisAsync(
        int professorId,
        [FromQuery] DateTimeOffset? inicioUtc,
        [FromQuery] DateTimeOffset? fimUtc,
        CancellationToken cancellationToken)
    {
        if (!inicioUtc.HasValue || !fimUtc.HasValue)
        {
            return BadRequest(new { message = "Informe o início e o fim do período em UTC." });
        }

        var resultado = await agendaAulasService.ListarHorariosDisponiveisAsync(
            professorId, inicioUtc.Value, fimUtc.Value, cancellationToken);
        return resultado.Status switch
        {
            AgendaOperacaoStatus.Concluida => Ok(resultado.Valor),
            AgendaOperacaoStatus.ProfessorNaoEncontrado => NotFound(),
            AgendaOperacaoStatus.TarifaInvalida => Conflict(new { message = "O preço da hora-aula deve ser um valor positivo em reais inteiros para converter em pontos." }),
            AgendaOperacaoStatus.FusoHorarioInvalido => Conflict(new { message = "O fuso horário da agenda do professor está inválido." }),
            _ => BadRequest(new { message = "O período solicitado é inválido ou excede 31 dias." })
        };
    }

    [HttpGet("professores/{professorId:int}/agenda")]
    public async Task<ActionResult<IReadOnlyList<HorarioAgendaResponse>>> ListarAgendaProfessorAsync(
        int professorId,
        [FromQuery] DateTimeOffset? inicioUtc,
        [FromQuery] DateTimeOffset? fimUtc,
        CancellationToken cancellationToken)
    {
        if (!inicioUtc.HasValue || !fimUtc.HasValue)
        {
            return BadRequest(new { message = "Informe o início e o fim do período em UTC." });
        }

        var resultado = await agendaAulasService.ListarAgendaProfessorAsync(
            professorId, inicioUtc.Value, fimUtc.Value, cancellationToken);
        return resultado.Status switch
        {
            AgendaOperacaoStatus.Concluida => Ok(resultado.Valor),
            AgendaOperacaoStatus.ProfessorNaoEncontrado => NotFound(),
            AgendaOperacaoStatus.TarifaInvalida => Conflict(new { message = "O preço da hora-aula deve ser um valor positivo em reais inteiros para converter em pontos." }),
            AgendaOperacaoStatus.FusoHorarioInvalido => Conflict(new { message = "O fuso horário da agenda do professor está inválido." }),
            _ => BadRequest(new { message = "O período solicitado é inválido ou excede 31 dias." })
        };
    }

    [HttpPost("aulas")]
    public async Task<ActionResult<AulaAgendadaResponse>> ReservarAsync(
        ReservarAulaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryObterContaAcessoId(out var contaAcessoId))
        {
            return Unauthorized();
        }

        var resultado = await agendaAulasService.ReservarAsync(contaAcessoId, request, cancellationToken);
        return resultado.Status switch
        {
            AgendaOperacaoStatus.Concluida => CreatedAtRoute(
                nameof(ObterMinhaAulaAsync),
                new { id = resultado.Valor!.Id },
                resultado.Valor),
            AgendaOperacaoStatus.EntradaInvalida => BadRequest(new { message = "Os dados da reserva são inválidos." }),
            AgendaOperacaoStatus.ContaNaoEncontrada or AgendaOperacaoStatus.ProfessorNaoEncontrado => NotFound(),
            AgendaOperacaoStatus.PerfilIncorreto => Forbid(),
            AgendaOperacaoStatus.TarifaInvalida => Conflict(new { message = "O preço da hora-aula deve ser um valor positivo em reais inteiros." }),
            AgendaOperacaoStatus.HorarioIndisponivel => Conflict(new { message = "O horário não está na disponibilidade semanal do professor." }),
            AgendaOperacaoStatus.HorarioOcupado => Conflict(new { message = "O horário acabou de ser reservado ou conflita com outra aula." }),
            AgendaOperacaoStatus.SaldoInsuficiente => StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Saldo de pontos insuficiente." }),
            AgendaOperacaoStatus.SaldoMaximoAtingido => Conflict(new { message = "O saldo de pontos do professor atingiu o limite." }),
            AgendaOperacaoStatus.IdempotenciaReutilizada => Conflict(new { message = "A chave de idempotência já foi usada em outra reserva." }),
            _ => BadRequest()
        };
    }

    [HttpGet("minhas-aulas")]
    public async Task<ActionResult<IReadOnlyList<AulaAgendadaResponse>>> ListarMinhasAsync(CancellationToken cancellationToken)
    {
        if (!TryObterContaAcessoId(out var contaAcessoId))
        {
            return Unauthorized();
        }

        return Ok(await agendaAulasService.ListarMinhasAsync(contaAcessoId, cancellationToken));
    }

    [HttpGet("aulas/{id:long}", Name = nameof(ObterMinhaAulaAsync))]
    public async Task<ActionResult<AulaAgendadaResponse>> ObterMinhaAulaAsync(long id, CancellationToken cancellationToken)
    {
        if (!TryObterContaAcessoId(out var contaAcessoId))
        {
            return Unauthorized();
        }

        var aula = await agendaAulasService.ObterMinhaAulaAsync(contaAcessoId, id, cancellationToken);
        return aula is null ? NotFound() : Ok(aula);
    }

    private bool TryObterContaAcessoId(out int contaAcessoId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out contaAcessoId);
}
