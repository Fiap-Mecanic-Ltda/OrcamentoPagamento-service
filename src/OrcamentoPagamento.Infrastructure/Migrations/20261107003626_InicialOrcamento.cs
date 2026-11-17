using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrcamentoPagamento.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InicialOrcamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orcamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdemServicoId = table.Column<int>(type: "int", nullable: false),
                    ValorTotalPecas = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorTotalInsumos = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorTotalServicos = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorTotalGeral = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DataGeracao = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Validade = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AjustadoManualmente = table.Column<bool>(type: "bit", nullable: false),
                    AjustadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimoEventoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UltimoEventoEm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orcamentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItensOrcamento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrcamentoId = table.Column<int>(type: "int", nullable: false),
                    ItemOrigemId = table.Column<int>(type: "int", nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Categoria = table.Column<int>(type: "int", nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensOrcamento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensOrcamento_Orcamentos_OrcamentoId",
                        column: x => x.OrcamentoId,
                        principalTable: "Orcamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrdemServicoSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrcamentoId = table.Column<int>(type: "int", nullable: false),
                    DescricaoProblema = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StatusOrdemServico = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClienteNome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClienteEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClienteTelefone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    VeiculoMarca = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VeiculoModelo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VeiculoPlaca = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    VeiculoAno = table.Column<int>(type: "int", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdemServicoSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrdemServicoSnapshots_Orcamentos_OrcamentoId",
                        column: x => x.OrcamentoId,
                        principalTable: "Orcamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItensOrcamento_OrcamentoId",
                table: "ItensOrcamento",
                column: "OrcamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Orcamentos_OrdemServicoId",
                table: "Orcamentos",
                column: "OrdemServicoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdemServicoSnapshots_OrcamentoId",
                table: "OrdemServicoSnapshots",
                column: "OrcamentoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItensOrcamento");

            migrationBuilder.DropTable(
                name: "OrdemServicoSnapshots");

            migrationBuilder.DropTable(
                name: "Orcamentos");
        }
    }
}
