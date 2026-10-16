using APILinguagem.Data;
using APILinguagem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace APILinguagem.Services;

public sealed class ContaAcessoService(AlunosDbContext dbContext)
{
    private readonly PasswordHasher<ContaAcesso> _hasher = new();

    public async Task<bool> DefinirSenhaAsync(
        string email,
        string nome,
        string tipoPerfil,
        int perfilId,
        string senha,
        CancellationToken cancellationToken)
    {
        var emailNormalizado = NormalizarEmail(email);
        var conta = await dbContext.ContasAcesso
            .SingleOrDefaultAsync(item => item.EmailNormalizado == emailNormalizado, cancellationToken);

        if (conta is null)
        {
            conta = new ContaAcesso
            {
                Email = email.Trim(),
                EmailNormalizado = emailNormalizado,
                Nome = nome,
                TipoPerfil = tipoPerfil,
                PerfilId = perfilId
            };
            dbContext.ContasAcesso.Add(conta);
        }
        else
        {
            conta.Email = email.Trim();
            conta.Nome = nome;
            conta.TipoPerfil = tipoPerfil;
            conta.PerfilId = perfilId;
        }

        conta.SenhaHash = _hasher.HashPassword(conta, senha);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> VerificarSenhaAsync(string email, string senha, CancellationToken cancellationToken)
    {
        var emailNormalizado = NormalizarEmail(email);
        var conta = await dbContext.ContasAcesso
            .SingleOrDefaultAsync(item => item.EmailNormalizado == emailNormalizado, cancellationToken);

        if (conta is null)
        {
            return false;
        }

        return _hasher.VerifyHashedPassword(conta, conta.SenhaHash, senha)
            != PasswordVerificationResult.Failed;
    }

    public static string NormalizarEmail(string email) => email.Trim().ToUpperInvariant();
}
