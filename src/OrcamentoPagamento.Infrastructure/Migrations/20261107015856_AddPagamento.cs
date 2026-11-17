using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrcamentoPagamento.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPagamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pagamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequisicaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrcamentoId = table.Column<int>(type: "int", nullable: false),
                    OrdemServicoId = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StatusDetalhe = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Provedor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IdExterno = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Parcelas = table.Column<int>(type: "int", nullable: false),
                    MetodoPagamento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ModoTeste = table.Column<bool>(type: "bit", nullable: false),
                    UltimaAtualizacaoProvedor = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pagamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pagamentos_Orcamentos_OrcamentoId",
                        column: x => x.OrcamentoId,
                        principalTable: "Orcamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_IdExterno",
                table: "Pagamentos",
                column: "IdExterno",
                unique: true,
                filter: "[IdExterno] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_OrcamentoId",
                table: "Pagamentos",
                column: "OrcamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_OrdemServicoId",
                table: "Pagamentos",
                column: "OrdemServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_RequisicaoId",
                table: "Pagamentos",
                column: "RequisicaoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pagamentos");
        }
    }
}
