namespace APILinguagem.Models;

public sealed class ContaAcesso
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string EmailNormalizado { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string TipoPerfil { get; set; } = string.Empty;
    public int PerfilId { get; set; }
    public string SenhaHash { get; set; } = string.Empty;
}
