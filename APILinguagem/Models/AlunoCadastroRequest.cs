using System.ComponentModel.DataAnnotations;
using professor.Model;

namespace APILinguagem.Models;

public sealed class AlunoCadastroRequest
{
    [Required]
    public Alunos Aluno { get; init; } = new();

    [Required, MinLength(8), MaxLength(128)]
    public string Senha { get; init; } = string.Empty;

    [MaxLength(2_097_152)]
    public byte[]? FotoPerfil { get; init; }

    [MaxLength(100)]
    public string? FotoPerfilContentType { get; init; }

    public string TurnstileToken { get; init; } = string.Empty;
}
