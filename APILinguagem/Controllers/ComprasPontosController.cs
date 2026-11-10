using System.Security.Claims;
using APILinguagem.Data;
using APILinguagem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/compras-pontos")]
[Authorize]
public sealed class ComprasPontosController(
    AlunosDbContext dbContext,
    CompraPontosService compraPontosService,
    ILogger<ComprasPontosController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CompraPontosResponse>> CriarAsync(
        [FromBody] CriarCompraPontosRequest request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var contaAcessoId))
        {
            return Unauthorized();
        }

        var tipoPerfil = await dbContext.ContasAcesso
            .AsNoTracking()
            .Where(conta => conta.Id == contaAcessoId)
            .Select(conta => conta.TipoPerfil)
            .SingleOrDefaultAsync(cancellationToken);
        if (tipoPerfil != "aluno")
        {
            return Forbid();
        }

        try
        {
            var compra = await compraPontosService.CriarAsync(contaAcessoId, request.PacoteId, cancellationToken);
            return compra is null
                ? NotFound(new { message = "O pacote não existe ou está inativo." })
                : CreatedAtAction(nameof(ObterAsync), new { id = compra.Id }, compra);
        }
        catch (FalhaGerarPixException exception)
        {
            logger.LogError(exception, "Falha ao gerar a cobrança Pix da compra {CompraId}.", exception.CompraId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "A compra foi registrada, mas não foi possível gerar o Pix. Tente gerar a cobrança novamente.",
                compraId = exception.CompraId
            });
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "A integração Pix não está configurada corretamente.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "A integração Pix está indisponível no momento." });
        }
    }

    [HttpPost("{id:long}/pix")]
    public async Task<ActionResult<CompraPontosResponse>> RecriarPixAsync(
        long id,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var contaAcessoId))
        {
            return Unauthorized();
        }

        try
        {
            var compra = await compraPontosService.RecriarPixAsync(contaAcessoId, id, cancellationToken);
            return compra is null
                ? NotFound(new { message = "A compra não existe, não pertence à sua conta ou já expirou." })
                : Ok(compra);
        }
        catch (FalhaGerarPixException exception)
        {
            logger.LogError(exception, "Falha ao gerar novamente o Pix da compra {CompraId}.", exception.CompraId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Não foi possível gerar o Pix agora. Tente novamente." });
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "A integração Pix não está configurada corretamente.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "A integração Pix está indisponível no momento." });
        }
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CompraPontosResponse>> ObterAsync(long id, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var contaAcessoId))
        {
            return Unauthorized();
        }

        var compra = await compraPontosService.ObterAsync(contaAcessoId, id, cancellationToken);
        return compra is null ? NotFound() : Ok(compra);
    }

    [AllowAnonymous]
    [HttpPost("webhook/inter")]
    public async Task<IActionResult> ReceberWebhookAsync(
        [FromBody] InterPixWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var txids = request.Pix?
            .Select(pagamento => pagamento.Txid)
            .Where(txid => txid is { Length: >= 26 and <= 35 })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (txids is null || txids.Length == 0)
        {
            return BadRequest(new { message = "A notificação não contém txids Pix válidos." });
        }

        foreach (var txid in txids)
        {
            await compraPontosService.ConfirmarPagamentoAsync(txid!, cancellationToken);
        }

        return Ok();
    }

    [HttpPut("webhook/configurar")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> ConfigurarWebhookAsync(CancellationToken cancellationToken)
    {
        try
        {
            await compraPontosService.ConfigurarWebhookAsync(cancellationToken);
            return NoContent();
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Falha ao registrar o webhook Pix no Inter.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Não foi possível registrar o webhook Pix no Inter." });
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "A configuração do webhook Pix é inválida.");
            return BadRequest(new { message = "Configure a URL HTTPS pública do webhook na API." });
        }
    }
}

public sealed record CriarCompraPontosRequest(int PacoteId);
public sealed record InterPixWebhookRequest(IReadOnlyList<InterPixWebhookPagamento>? Pix);
public sealed record InterPixWebhookPagamento(string? Txid);