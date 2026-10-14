using Microsoft.EntityFrameworkCore;
using professor.Model;

namespace APILinguagem.Data;

public sealed class AlunosDbContext(DbContextOptions<AlunosDbContext> options) : DbContext(options)
{
    public DbSet<Alunos> Alunos => Set<Alunos>();
}
