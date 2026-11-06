using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace APILinguagem.Migrations
{
    /// <inheritdoc />
    public partial class AddPacotesPontos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PacotesPontos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QuantidadePontos = table.Column<int>(type: "integer", nullable: false),
                    PrecoCentavos = table.Column<int>(type: "integer", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PacotesPontos", x => x.Id);
                    table.CheckConstraint("CK_PacotesPontos_PrecoCentavos_Positive", "\"PrecoCentavos\" > 0");
                    table.CheckConstraint("CK_PacotesPontos_QuantidadePontos_Positive", "\"QuantidadePontos\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PacotesPontos_Nome",
                table: "PacotesPontos",
                column: "Nome",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PacotesPontos");
        }
    }
}
