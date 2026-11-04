using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APILinguagem.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "FotoPerfil",
                table: "Professores",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FotoPerfilContentType",
                table: "Professores",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FotoPerfil",
                table: "Alunos",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FotoPerfilContentType",
                table: "Alunos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FotoPerfil",
                table: "Professores");

            migrationBuilder.DropColumn(
                name: "FotoPerfilContentType",
                table: "Professores");

            migrationBuilder.DropColumn(
                name: "FotoPerfil",
                table: "Alunos");

            migrationBuilder.DropColumn(
                name: "FotoPerfilContentType",
                table: "Alunos");
        }
    }
}
