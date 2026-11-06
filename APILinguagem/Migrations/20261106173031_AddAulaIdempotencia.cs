using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APILinguagem.Migrations
{
    /// <inheritdoc />
    public partial class AddAulaIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChaveIdempotencia",
                table: "AulasAgendadas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_AulasAgendadas_ContaAcessoAlunoId_ChaveIdempotencia",
                table: "AulasAgendadas",
                columns: new[] { "ContaAcessoAlunoId", "ChaveIdempotencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AulasAgendadas_ContaAcessoAlunoId_ChaveIdempotencia",
                table: "AulasAgendadas");

            migrationBuilder.DropColumn(
                name: "ChaveIdempotencia",
                table: "AulasAgendadas");
        }
    }
}
