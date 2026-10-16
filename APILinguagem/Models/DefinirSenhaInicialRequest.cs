using System.ComponentModel.DataAnnotations;

namespace APILinguagem.Models;

public sealed class DefinirSenhaInicialRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string Senha { get; init; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string ChaveAdministrativa { get; init; } = string.Empty;
}
