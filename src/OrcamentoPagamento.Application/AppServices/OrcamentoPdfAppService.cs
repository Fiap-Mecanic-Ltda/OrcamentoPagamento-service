using OrcamentoPagamento.Application.AppServices.Interfaces;
using OrcamentoPagamento.Application.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OrcamentoPagamento.Application.AppServices
{
    /// <summary>
    /// Mesma identidade visual do PDF do monólito, acrescentando a tabela de itens — aqui
    /// o serviço tem os itens no próprio banco, então não precisa pedir à Oficina.
    /// </summary>
    public class OrcamentoPdfAppService : IOrcamentoPdfAppService
    {
        private const string SemInformacao = "—";

        public byte[] Gerar(OrcamentoPdfDto dto)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(t => t.FontSize(10).FontFamily("DejaVu Sans"));
                    page.Header().Element(c => ConstruirCabecalho(c, dto));
                    page.Content().Element(c => ConstruirConteudo(c, dto));
                    page.Footer().Element(ConstruirRodape);
                });
            }).GeneratePdf();
        }

        // ─── Cabeçalho ──────────────────────────────────────────────────────────

        private static void ConstruirCabecalho(IContainer container, OrcamentoPdfDto dto)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item()
                         .Text("MechanicLtda")
                         .Bold()
                         .FontSize(20)
                         .FontColor(Colors.Blue.Darken3);

                        c.Item()
                         .Text("Oficina Mecânica")
                         .FontSize(11)
                         .FontColor(Colors.Grey.Darken1);
                    });

                    row.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item()
                         .Text($"ORÇAMENTO Nº {dto.Id:D6}")
                         .Bold()
                         .FontSize(14)
                         .FontColor(Colors.Blue.Darken3);

                        c.Item()
                         .Text($"Data de Geração: {dto.DataGeracao:dd/MM/yyyy}")
                         .FontSize(9);

                        c.Item()
                         .Text(dto.Validade.HasValue
                             ? $"Válido até: {dto.Validade:dd/MM/yyyy}"
                             : "Sem validade definida")
                         .FontSize(9)
                         .FontColor(dto.Validade.HasValue ? Colors.Green.Darken2 : Colors.Grey.Darken1);
                    });
                });

                col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(Colors.Blue.Darken3);
            });
        }

        // ─── Conteúdo ───────────────────────────────────────────────────────────

        private static void ConstruirConteudo(IContainer container, OrcamentoPdfDto dto)
        {
            container.PaddingTop(16).Column(col =>
            {
                col.Item().Element(c => ConstruirSecao(c, "Dados do Cliente", inner =>
                {
                    inner.Item().Row(row =>
                    {
                        ColunaInfo(row, "Nome", dto.ClienteNome);
                        ColunaInfo(row, "E-mail", dto.ClienteEmail);
                        ColunaInfo(row, "Telefone", dto.ClienteTelefone);
                    });
                }));

                col.Item().PaddingTop(12).Element(c => ConstruirSecao(c, "Dados do Veículo", inner =>
                {
                    inner.Item().Row(row =>
                    {
                        ColunaInfo(row, "Marca", dto.VeiculoMarca);
                        ColunaInfo(row, "Modelo", dto.VeiculoModelo);
                        ColunaInfo(row, "Placa", dto.VeiculoPlaca);
                        ColunaInfo(row, "Ano", dto.VeiculoAno?.ToString());
                    });
                }));

                col.Item().PaddingTop(12).Element(c => ConstruirSecao(c, "Ordem de Serviço", inner =>
                {
                    inner.Item().Row(row =>
                    {
                        ColunaInfo(row, "Nº OS", dto.OrdemServicoId.ToString());
                        ColunaInfo(row, "Status", dto.StatusOrdemServico);
                    });

                    inner.Item().PaddingTop(6).Column(c =>
                    {
                        c.Item().Text("Descrição do Problema:").Bold().FontSize(9);
                        c.Item()
                         .PaddingTop(2)
                         .Background(Colors.Grey.Lighten3)
                         .Padding(6)
                         .Text(string.IsNullOrWhiteSpace(dto.DescricaoProblema) ? SemInformacao : dto.DescricaoProblema)
                         .FontSize(9);
                    });
                }));

                if (dto.Itens.Count > 0)
                    col.Item().PaddingTop(12).Element(c => ConstruirSecao(c, "Itens", ConstruirTabelaItens(dto)));

                col.Item().PaddingTop(12).Element(c => ConstruirSecao(c, "Resumo Financeiro", inner =>
                {
                    inner.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(1);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background(Colors.Blue.Darken3).Padding(6)
                             .Text("Descrição").Bold().FontColor(Colors.White);

                            h.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight()
                             .Text("Valor (R$)").Bold().FontColor(Colors.White);
                        });

                        LinhaTabela(table, "Total em Peças", dto.ValorTotalPecas, false);
                        LinhaTabela(table, "Total em Insumos", dto.ValorTotalInsumos, true);
                        LinhaTabela(table, "Total em Serviços", dto.ValorTotalServicos, false);

                        table.Cell()
                             .Background(Colors.Blue.Lighten4)
                             .Padding(6)
                             .Text("TOTAL GERAL")
                             .Bold();

                        table.Cell()
                             .Background(Colors.Blue.Lighten4)
                             .Padding(6)
                             .AlignRight()
                             .Text(dto.ValorTotalGeral.ToString("C2"))
                             .Bold()
                             .FontColor(Colors.Blue.Darken3);
                    });
                }));

                col.Item().PaddingTop(40).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Darken1);
                        c.Item().PaddingTop(4).AlignCenter().Text("Assinatura do Responsável").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    row.ConstantItem(40);

                    row.RelativeItem().Column(c =>
                    {
                        c.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Darken1);
                        c.Item().PaddingTop(4).AlignCenter().Text("Assinatura do Cliente").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }

        private static Action<ColumnDescriptor> ConstruirTabelaItens(OrcamentoPdfDto dto)
        {
            return inner =>
            {
                inner.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(4);
                        cols.RelativeColumn(0.9f);
                        cols.RelativeColumn(0.6f);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(1);
                    });

                    table.Header(h =>
                    {
                        CelulaCabecalho(h.Cell(), "Descrição", false);
                        CelulaCabecalho(h.Cell(), "Categoria", false);
                        CelulaCabecalho(h.Cell(), "Qtd.", true);
                        CelulaCabecalho(h.Cell(), "Unitário", true);
                        CelulaCabecalho(h.Cell(), "Total", true);
                    });

                    var cinza = false;
                    foreach (var item in dto.Itens)
                    {
                        var bg = cinza ? Colors.Grey.Lighten4 : Colors.White;

                        table.Cell().Background(bg).Padding(5).Text(item.Descricao).FontSize(9);
                        table.Cell().Background(bg).Padding(5).Text(item.Categoria.ToString()).FontSize(9);
                        table.Cell().Background(bg).Padding(5).AlignRight().Text(item.Quantidade.ToString()).FontSize(9);
                        table.Cell().Background(bg).Padding(5).AlignRight().Text(item.ValorUnitario.ToString("C2")).FontSize(9);
                        table.Cell().Background(bg).Padding(5).AlignRight().Text(item.ValorTotal.ToString("C2")).FontSize(9);

                        cinza = !cinza;
                    }
                });
            };
        }

        // ─── Rodapé ─────────────────────────────────────────────────────────────

        private static void ConstruirRodape(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem()
                       .Text("MechanicLtda — Documento gerado automaticamente.")
                       .FontSize(8)
                       .FontColor(Colors.Grey.Darken1);

                    row.RelativeItem()
                       .AlignRight()
                       .Text(text =>
                       {
                           text.Span("Página ").FontSize(8).FontColor(Colors.Grey.Darken1);
                           text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                           text.Span(" de ").FontSize(8).FontColor(Colors.Grey.Darken1);
                           text.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                       });
                });
            });
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private static void ConstruirSecao(IContainer container, string titulo, Action<ColumnDescriptor> conteudo)
        {
            container.Column(col =>
            {
                col.Item()
                   .Background(Colors.Blue.Lighten4)
                   .Padding(6)
                   .Text(titulo)
                   .Bold()
                   .FontSize(10)
                   .FontColor(Colors.Blue.Darken3);

                col.Item()
                   .Border(0.5f)
                   .BorderColor(Colors.Grey.Lighten1)
                   .Padding(8)
                   .Column(conteudo);
            });
        }

        private static void ColunaInfo(RowDescriptor row, string label, string? valor)
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(label).Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                c.Item().Text(string.IsNullOrWhiteSpace(valor) ? SemInformacao : valor).FontSize(10);
            });
        }

        private static void CelulaCabecalho(IContainer header, string texto, bool alinharDireita)
        {
            var celula = header.Background(Colors.Blue.Darken3).Padding(5);

            if (alinharDireita)
                celula = celula.AlignRight();

            celula.Text(texto).Bold().FontSize(9).FontColor(Colors.White);
        }

        private static void LinhaTabela(TableDescriptor table, string descricao, decimal valor, bool cinza)
        {
            var bg = cinza ? Colors.Grey.Lighten4 : Colors.White;

            table.Cell().Background(bg).Padding(6).Text(descricao);
            table.Cell().Background(bg).Padding(6).AlignRight().Text(valor.ToString("C2"));
        }
    }
}
