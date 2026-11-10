using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APILinguagem.Migrations
{
    /// <inheritdoc />
    public partial class AddPixCompraDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DataPagamento",
                table: "ComprasPontos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentificadorCobranca",
                table: "ComprasPontos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PacotePontosId",
                table: "ComprasPontos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixCopiaECola",
                table: "ComprasPontos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrCodeBase64",
                table: "ComprasPontos",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComprasPontos_PacotePontosId",
                table: "ComprasPontos",
                column: "PacotePontosId");

            migrationBuilder.AddForeignKey(
                name: "FK_ComprasPontos_PacotesPontos_PacotePontosId",
                table: "ComprasPontos",
                column: "PacotePontosId",
                principalTable: "PacotesPontos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ComprasPontos_PacotesPontos_PacotePontosId",
                table: "ComprasPontos");

            migrationBuilder.DropIndex(
                name: "IX_ComprasPontos_PacotePontosId",
                table: "ComprasPontos");

            migrationBuilder.DropColumn(
                name: "DataPagamento",
                table: "ComprasPontos");

            migrationBuilder.DropColumn(
                name: "IdentificadorCobranca",
                table: "ComprasPontos");

            migrationBuilder.DropColumn(
                name: "PacotePontosId",
                table: "ComprasPontos");

            migrationBuilder.DropColumn(
                name: "PixCopiaECola",
                table: "ComprasPontos");

            migrationBuilder.DropColumn(
                name: "QrCodeBase64",
                table: "ComprasPontos");
        }
    }
}
