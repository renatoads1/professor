using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace professor.Model
{
    public class Alunos
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Informe o nome.")]
        public string Nome { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe o e-mail.")]
        [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe o telefone.")]
        public string Telefone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe o país.")]
        public string Pais { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe o estado.")]
        public string Estado { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe a cidade.")]
        public string Cidade { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe a língua de interesse.")]
        public string LinguaDeInteresse { get; set; } = string.Empty;

        public bool Estudando { get; set; }

        public bool TermosAceitos { get; set; }

        [JsonIgnore]
        public byte[]? FotoPerfil { get; set; }

        [JsonIgnore]
        public string? FotoPerfilContentType { get; set; }
    }
}
