using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace APILinguagem.Migrations
{
    /// <inheritdoc />
    public partial class AddComprasPontos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComprasPontos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContaAcessoId = table.Column<int>(type: "integer", nullable: false),
                    QuantidadePontos = table.Column<int>(type: "integer", nullable: false),
                    PrecoCentavos = table.Column<int>(type: "integer", nullable: false),
                    IdentificadorProvedor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprasPontos", x => x.Id);
                    table.CheckConstraint("CK_ComprasPontos_PrecoCentavos_Positive", "\"PrecoCentavos\" > 0");
                    table.CheckConstraint("CK_ComprasPontos_QuantidadePontos_Positive", "\"QuantidadePontos\" > 0");
                    table.ForeignKey(
                        name: "FK_ComprasPontos_ContasAcesso_ContaAcessoId",
                        column: x => x.ContaAcessoId,
                        principalTable: "ContasAcesso",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprasPontos_ContaAcessoId_CriadaEm",
                table: "ComprasPontos",
                columns: new[] { "ContaAcessoId", "CriadaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_ComprasPontos_IdentificadorProvedor",
                table: "ComprasPontos",
                column: "IdentificadorProvedor",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComprasPontos");
        }
    }
}
