using Microsoft.EntityFrameworkCore;
using professor.Model;

namespace APILinguagem.Data;

public sealed class AlunosDbContext(DbContextOptions<AlunosDbContext> options) : DbContext(options)
{
    public DbSet<Alunos> Alunos => Set<Alunos>();
    public DbSet<Professores> Professores => Set<Professores>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Professores>(entity =>
        {
            entity.ToTable("Professores");
            entity.HasKey(professor => professor.Id);
            entity.HasIndex(professor => professor.Email).IsUnique();
            entity.Property(professor => professor.LinguaDeEnsino).IsRequired();
            entity.Property(professor => professor.Apresentacao).HasMaxLength(1000);
        });
    }
}
