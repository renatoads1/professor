namespace APILinguagem.Models;

public sealed record LoginResponse(string Token, DateTimeOffset ExpiraEm, string Nome, string Email);
