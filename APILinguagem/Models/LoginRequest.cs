using System.ComponentModel.DataAnnotations;

namespace APILinguagem.Models;

public sealed class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(1), MaxLength(128)]
    public string Senha { get; init; } = string.Empty;
}
