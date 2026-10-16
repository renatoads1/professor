using System.ComponentModel.DataAnnotations;
using professor.Model;

namespace APILinguagem.Models;

public sealed class AlunoCadastroRequest
{
    [Required]
    public Alunos Aluno { get; init; } = new();

    public string TurnstileToken { get; init; } = string.Empty;
}
