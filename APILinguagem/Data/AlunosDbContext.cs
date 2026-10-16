using Microsoft.EntityFrameworkCore;
using professor.Model;
using APILinguagem.Models;

namespace APILinguagem.Data;

public sealed class AlunosDbContext(DbContextOptions<AlunosDbContext> options) : DbContext(options)
{
    public DbSet<Alunos> Alunos => Set<Alunos>();
    public DbSet<Professores> Professores => Set<Professores>();
    public DbSet<ContaAcesso> ContasAcesso => Set<ContaAcesso>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Professores>(entity =>
        {
            entity.ToTable("Professores");
            entity.HasKey(professor => professor.Id);
            entity.HasIndex(professor => professor.Email).IsUnique();
            entity.Property(professor => professor.LinguaDeEnsino).IsRequired();
            entity.Property(professor => professor.HoraAula).HasPrecision(8, 2);
            entity.Property(professor => professor.Apresentacao).HasMaxLength(1000);
        });

        modelBuilder.Entity<ContaAcesso>(entity =>
        {
            entity.ToTable("ContasAcesso");
            entity.HasKey(conta => conta.Id);
            entity.HasIndex(conta => conta.EmailNormalizado).IsUnique();
            entity.Property(conta => conta.Email).IsRequired();
            entity.Property(conta => conta.EmailNormalizado).IsRequired();
            entity.Property(conta => conta.Nome).IsRequired();
            entity.Property(conta => conta.TipoPerfil).IsRequired();
            entity.Property(conta => conta.SenhaHash).IsRequired();
        });
    }
}
