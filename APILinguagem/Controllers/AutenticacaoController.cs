using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using APILinguagem.Data;
using APILinguagem.Models;
using APILinguagem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace APILinguagem.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AutenticacaoController(
    AlunosDbContext dbContext,
    ContaAcessoService contaAcessoService,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var emailNormalizado = ContaAcessoService.NormalizarEmail(request.Email);
        var conta = await dbContext.ContasAcesso
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.EmailNormalizado == emailNormalizado, cancellationToken);

        if (conta is null || !await contaAcessoService.VerificarSenhaAsync(request.Email, request.Senha, cancellationToken))
        {
            return Unauthorized(new { message = "E-mail ou senha inválidos." });
        }

        var chave = ObterChaveToken();
        if (chave is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "A autenticação não está configurada no servidor." });
        }

        var agora = DateTimeOffset.UtcNow;
        var expiracao = agora.AddHours(8);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, conta.Id.ToString()),
            new Claim(ClaimTypes.Name, conta.Nome),
            new Claim(ClaimTypes.Email, conta.Email)
        };
        var credenciais = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: configuration["Authentication:Issuer"] ?? "professor-api",
            audience: configuration["Authentication:Audience"] ?? "professor-client",
            claims: claims,
            notBefore: agora.UtcDateTime,
            expires: expiracao.UtcDateTime,
            signingCredentials: credenciais);

        return Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expiracao, conta.Nome, conta.Email));
    }

    private string? ObterChaveToken()
    {
        var chave = configuration["Authentication:JwtSigningKey"];
        return !string.IsNullOrWhiteSpace(chave) && Encoding.UTF8.GetByteCount(chave) >= 32
            ? chave
            : null;
    }

}
