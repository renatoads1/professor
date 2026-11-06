using Microsoft.EntityFrameworkCore;
using professor.Model;
using APILinguagem.Models;

namespace APILinguagem.Data;

public sealed class AlunosDbContext(DbContextOptions<AlunosDbContext> options) : DbContext(options)
{
    public DbSet<Alunos> Alunos => Set<Alunos>();
    public DbSet<Professores> Professores => Set<Professores>();
    public DbSet<ContaAcesso> ContasAcesso => Set<ContaAcesso>();
    public DbSet<LancamentoPontos> LancamentosPontos => Set<LancamentoPontos>();
    public DbSet<CompraPontos> ComprasPontos => Set<CompraPontos>();
    public DbSet<PacotePontos> PacotesPontos => Set<PacotePontos>();

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
            entity.Property(conta => conta.SaldoPontos).HasDefaultValue(0);
        });

        modelBuilder.Entity<LancamentoPontos>(entity =>
        {
            entity.ToTable("LancamentosPontos", table =>
                table.HasCheckConstraint("CK_LancamentosPontos_Quantidade_NaoZero", "\"Quantidade\" <> 0"));
            entity.HasKey(lancamento => lancamento.Id);
            entity.HasIndex(lancamento => new { lancamento.ContaAcessoId, lancamento.ChaveIdempotencia }).IsUnique();
            entity.Property(lancamento => lancamento.Motivo).HasMaxLength(200).IsRequired();
            entity.Property(lancamento => lancamento.ChaveIdempotencia).HasMaxLength(100).IsRequired();
            entity.Property(lancamento => lancamento.DataLancamento).IsRequired();
            entity.HasOne(lancamento => lancamento.ContaAcesso)
                .WithMany()
                .HasForeignKey(lancamento => lancamento.ContaAcessoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompraPontos>(entity =>
        {
            entity.ToTable("ComprasPontos", table =>
            {
                table.HasCheckConstraint("CK_ComprasPontos_QuantidadePontos_Positive", "\"QuantidadePontos\" > 0");
                table.HasCheckConstraint("CK_ComprasPontos_PrecoCentavos_Positive", "\"PrecoCentavos\" > 0");
            });
            entity.HasKey(compra => compra.Id);
            entity.HasIndex(compra => compra.IdentificadorProvedor).IsUnique();
            entity.HasIndex(compra => new { compra.ContaAcessoId, compra.CriadaEm });
            entity.Property(compra => compra.IdentificadorProvedor).HasMaxLength(200).IsRequired();
            entity.Property(compra => compra.Status).HasMaxLength(30).IsRequired();
            entity.Property(compra => compra.CriadaEm).IsRequired();
            entity.Property(compra => compra.ExpiraEm).IsRequired();
            entity.HasOne(compra => compra.ContaAcesso)
                .WithMany()
                .HasForeignKey(compra => compra.ContaAcessoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PacotePontos>(entity =>
        {
            entity.ToTable("PacotesPontos", table =>
            {
                table.HasCheckConstraint("CK_PacotesPontos_QuantidadePontos_Positive", "\"QuantidadePontos\" > 0");
                table.HasCheckConstraint("CK_PacotesPontos_PrecoCentavos_Positive", "\"PrecoCentavos\" > 0");
            });
            entity.HasKey(pacote => pacote.Id);
            entity.HasIndex(pacote => pacote.Nome).IsUnique();
            entity.Property(pacote => pacote.Nome).HasMaxLength(100).IsRequired();
            entity.Property(pacote => pacote.CriadoEm).IsRequired();
        });
    }
}
