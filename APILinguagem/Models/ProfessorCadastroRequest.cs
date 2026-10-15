using System.ComponentModel.DataAnnotations;
using professor.Model;

namespace APILinguagem.Models;

public sealed class ProfessorCadastroRequest
{
    [Required]
    public Professores Professor { get; init; } = new();

    [Required]
    public string TurnstileToken { get; init; } = string.Empty;
}
