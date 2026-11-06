using professor.Model;

namespace APILinguagem.Models;

public sealed class AulaAgendada
{
    private AulaAgendada()
    {
    }

    public AulaAgendada(
        int professorId,
        int contaAcessoProfessorId,
        int contaAcessoAlunoId,
        DateTimeOffset inicioUtc,
        int quantidadePontos,
        string chaveIdempotencia)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(professorId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contaAcessoProfessorId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contaAcessoAlunoId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantidadePontos);
        if (string.IsNullOrWhiteSpace(chaveIdempotencia) || chaveIdempotencia.Trim().Length > 100)
        {
            throw new ArgumentException("A chave de idempotência deve ter entre 1 e 100 caracteres.", nameof(chaveIdempotencia));
        }

        InicioUtc = inicioUtc.ToUniversalTime();
        FimUtc = InicioUtc.AddHours(1);
        QuantidadePontos = quantidadePontos;
        ChaveIdempotencia = chaveIdempotencia.Trim();
        ProfessorId = professorId;
        ContaAcessoProfessorId = contaAcessoProfessorId;
        ContaAcessoAlunoId = contaAcessoAlunoId;
        Status = StatusAula.Agendada;
        CriadaEm = DateTimeOffset.UtcNow;
    }

    public long Id { get; private set; }
    public int ProfessorId { get; private set; }
    public Professores Professor { get; private set; } = null!;
    public int ContaAcessoProfessorId { get; private set; }
    public ContaAcesso ContaAcessoProfessor { get; private set; } = null!;
    public int ContaAcessoAlunoId { get; private set; }
    public ContaAcesso ContaAcessoAluno { get; private set; } = null!;
    public DateTimeOffset InicioUtc { get; private set; }
    public DateTimeOffset FimUtc { get; private set; }
    public int QuantidadePontos { get; private set; }
    public string ChaveIdempotencia { get; private set; } = string.Empty;
    public StatusAula Status { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
}

public enum StatusAula
{
    Agendada,
    EmAndamento,
    Concluida
}
