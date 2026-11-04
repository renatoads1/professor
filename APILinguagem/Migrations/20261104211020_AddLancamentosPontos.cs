using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace APILinguagem.Migrations
{
    /// <inheritdoc />
    public partial class AddLancamentosPontos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LancamentosPontos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContaAcessoId = table.Column<int>(type: "integer", nullable: false),
                    Quantidade = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataLancamento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LancamentosPontos", x => x.Id);
                    table.CheckConstraint("CK_LancamentosPontos_Quantidade_NaoZero", "\"Quantidade\" <> 0");
                    table.ForeignKey(
                        name: "FK_LancamentosPontos_ContasAcesso_ContaAcessoId",
                        column: x => x.ContaAcessoId,
                        principalTable: "ContasAcesso",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosPontos_ContaAcessoId_ChaveIdempotencia",
                table: "LancamentosPontos",
                columns: new[] { "ContaAcessoId", "ChaveIdempotencia" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION impedir_alteracao_lancamento_pontos()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'LancamentosPontos é uma tabela append-only; UPDATE e DELETE não são permitidos.';
                END;
                $$;

                CREATE TRIGGER TR_LancamentosPontos_Imutavel
                BEFORE UPDATE OR DELETE ON "LancamentosPontos"
                FOR EACH ROW
                EXECUTE FUNCTION impedir_alteracao_lancamento_pontos();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS TR_LancamentosPontos_Imutavel ON "LancamentosPontos";
                DROP FUNCTION IF EXISTS impedir_alteracao_lancamento_pontos();
                """);

            migrationBuilder.DropTable(
                name: "LancamentosPontos");
        }
    }
}
