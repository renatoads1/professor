using professor.Model;

namespace APILinguagem.Models;

public sealed class DisponibilidadeSemanalProfessor
{
    public long Id { get; set; }
    public int ProfessorId { get; set; }
    public Professores Professor { get; set; } = null!;
    public DayOfWeek DiaSemana { get; set; }
    public TimeOnly HoraInicio { get; set; }
    public TimeOnly HoraFim { get; set; }
    public string FusoHorarioId { get; set; } = string.Empty;
}
