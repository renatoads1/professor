using System.ComponentModel.DataAnnotations;
using professor.Model;

namespace APILinguagem.Models;

public sealed class ProfessorCadastroRequest
{
    [Required]
    public Professores Professor { get; init; } = new();

    [Required, MinLength(8), MaxLength(128)]
    public string Senha { get; init; } = string.Empty;

    public string TurnstileToken { get; init; } = string.Empty;
}
