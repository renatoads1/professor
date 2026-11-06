using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace APILinguagem.Migrations
{
    /// <inheritdoc />
    public partial class AddAgendaAulas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AulasAgendadas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfessorId = table.Column<int>(type: "integer", nullable: false),
                    ContaAcessoProfessorId = table.Column<int>(type: "integer", nullable: false),
                    ContaAcessoAlunoId = table.Column<int>(type: "integer", nullable: false),
                    InicioUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FimUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    QuantidadePontos = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AulasAgendadas", x => x.Id);
                    table.CheckConstraint("CK_AulasAgendadas_Intervalo", "\"FimUtc\" > \"InicioUtc\"");
                    table.CheckConstraint("CK_AulasAgendadas_QuantidadePontos_Positive", "\"QuantidadePontos\" > 0");
                    table.ForeignKey(
                        name: "FK_AulasAgendadas_ContasAcesso_ContaAcessoAlunoId",
                        column: x => x.ContaAcessoAlunoId,
                        principalTable: "ContasAcesso",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AulasAgendadas_ContasAcesso_ContaAcessoProfessorId",
                        column: x => x.ContaAcessoProfessorId,
                        principalTable: "ContasAcesso",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AulasAgendadas_Professores_ProfessorId",
                        column: x => x.ProfessorId,
                        principalTable: "Professores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisponibilidadesSemanaisProfessores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfessorId = table.Column<int>(type: "integer", nullable: false),
                    DiaSemana = table.Column<int>(type: "integer", nullable: false),
                    HoraInicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    HoraFim = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    FusoHorarioId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisponibilidadesSemanaisProfessores", x => x.Id);
                    table.CheckConstraint("CK_DisponibilidadesSemanaisProfessores_DiaSemana", "\"DiaSemana\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_DisponibilidadesSemanaisProfessores_Intervalo", "\"HoraFim\" > \"HoraInicio\"");
                    table.ForeignKey(
                        name: "FK_DisponibilidadesSemanaisProfessores_Professores_ProfessorId",
                        column: x => x.ProfessorId,
                        principalTable: "Professores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AulasAgendadas_ContaAcessoAlunoId_InicioUtc",
                table: "AulasAgendadas",
                columns: new[] { "ContaAcessoAlunoId", "InicioUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AulasAgendadas_ContaAcessoProfessorId_InicioUtc",
                table: "AulasAgendadas",
                columns: new[] { "ContaAcessoProfessorId", "InicioUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AulasAgendadas_ProfessorId_InicioUtc",
                table: "AulasAgendadas",
                columns: new[] { "ProfessorId", "InicioUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisponibilidadesSemanaisProfessores_ProfessorId_DiaSemana_H~",
                table: "DisponibilidadesSemanaisProfessores",
                columns: new[] { "ProfessorId", "DiaSemana", "HoraInicio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AulasAgendadas");

            migrationBuilder.DropTable(
                name: "DisponibilidadesSemanaisProfessores");
        }
    }
}
