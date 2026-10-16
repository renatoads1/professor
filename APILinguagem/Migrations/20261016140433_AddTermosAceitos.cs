using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APILinguagem.Migrations
{
    /// <inheritdoc />
    public partial class AddTermosAceitos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TermosAceitos",
                table: "Professores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TermosAceitos",
                table: "Alunos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TermosAceitos",
                table: "Professores");

            migrationBuilder.DropColumn(
                name: "TermosAceitos",
                table: "Alunos");
        }
    }
}
