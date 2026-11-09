using OrcamentoPagamento.Application.DTOs;
using OrcamentoPagamento.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OrcamentoPagamento.API.IntegrationTests
{
    public class OrcamentoEndpointsTests : IClassFixture<OrcamentoApiFactory>
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly OrcamentoApiFactory _factory;

        public OrcamentoEndpointsTests(OrcamentoApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Health_RespondeSemAutenticacao()
        {
            var client = _factory.CreateClient();

            var resposta = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }

        [Fact]
        public async Task ObterPorId_SemToken_Retorna401()
        {
            var client = _factory.CreateClient();

            var resposta = await client.GetAsync("/api/orcamento/1");

            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        }

        [Fact]
        public async Task ObterPorId_ComRoleCliente_Retorna403()
        {
            var client = _factory.CriarClienteAutenticado("Cliente");

            var resposta = await client.GetAsync("/api/orcamento/1");

            Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        }

        [Fact]
        public async Task ObterPorOrdemServico_SemOrcamento_Retorna404()
        {
            var client = _factory.CriarClienteAutenticado();

            var resposta = await client.GetAsync("/api/orcamento/ordem-servico/999999");

            Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        }

        [Fact]
        public async Task Recalcular_PrimeiroEvento_CriaOrcamentoComTotaisPorCategoria()
        {
            var client = _factory.CriarClienteAutenticado();
            var ordemServicoId = ProximaOrdemServico();

            var resposta = await client.PostAsJsonAsync("/api/orcamento/recalcular",
                NovoEvento(ordemServicoId, Guid.NewGuid(), DateTime.UtcNow));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

            var orcamento = await LerOrcamentoAsync(resposta);

            Assert.Equal(ordemServicoId, orcamento.OrdemServicoId);
            Assert.Equal(200m, orcamento.ValorTotalPecas);
            Assert.Equal(100m, orcamento.ValorTotalInsumos);
            Assert.Equal(150m, orcamento.ValorTotalServicos);
            Assert.Equal(450m, orcamento.ValorTotalGeral);
            Assert.Equal(3, orcamento.Itens.Count);
        }

        [Fact]
        public async Task Recalcular_MesmoEventoRepetido_Retorna204()
        {
            var client = _factory.CriarClienteAutenticado();
            var ordemServicoId = ProximaOrdemServico();
            var eventoId = Guid.NewGuid();
            var ocorridoEm = DateTime.UtcNow;

            var primeira = await client.PostAsJsonAsync("/api/orcamento/recalcular",
                NovoEvento(ordemServicoId, eventoId, ocorridoEm));
            Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);

            var segunda = await client.PostAsJsonAsync("/api/orcamento/recalcular",
                NovoEvento(ordemServicoId, eventoId, ocorridoEm));

            Assert.Equal(HttpStatusCode.NoContent, segunda.StatusCode);
        }

        [Fact]
        public async Task Recalcular_SemOrdemServicoId_Retorna400()
        {
            var client = _factory.CriarClienteAutenticado();

            var resposta = await client.PostAsJsonAsync("/api/orcamento/recalcular",
                NovoEvento(0, Guid.NewGuid(), DateTime.UtcNow));

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }

        [Fact]
        public async Task FluxoCompleto_RecalcularObterAjustarEExportarPdf()
        {
            var client = _factory.CriarClienteAutenticado();
            var ordemServicoId = ProximaOrdemServico();

            var criado = await client.PostAsJsonAsync("/api/orcamento/recalcular",
                NovoEvento(ordemServicoId, Guid.NewGuid(), DateTime.UtcNow));
            var orcamento = await LerOrcamentoAsync(criado);

            // Consulta pela OS, que é o caminho usado pela tela de detalhe da Ordem.
            var porOs = await client.GetAsync($"/api/orcamento/ordem-servico/{ordemServicoId}");
            Assert.Equal(HttpStatusCode.OK, porOs.StatusCode);
            Assert.Equal(orcamento.Id, (await LerOrcamentoAsync(porOs)).Id);

            // Ajuste manual: o total geral precisa virar a soma das três categorias.
            var ajuste = await client.PutAsJsonAsync($"/api/orcamento/{orcamento.Id}", new
            {
                valorTotalPecas = 300m,
                valorTotalInsumos = 120m,
                valorTotalServicos = 80m,
                validade = DateTime.UtcNow.AddDays(15)
            });

            Assert.Equal(HttpStatusCode.OK, ajuste.StatusCode);

            var ajustado = await LerOrcamentoAsync(ajuste);
            Assert.Equal(500m, ajustado.ValorTotalGeral);
            Assert.True(ajustado.AjustadoManualmente);

            // PDF: o snapshot gravado no recálculo é o que alimenta o relatório.
            var pdf = await client.GetAsync($"/api/orcamento/{orcamento.Id}/pdf");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 0);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }

        [Fact]
        public async Task AtualizarManual_ComValorNegativo_Retorna400()
        {
            var client = _factory.CriarClienteAutenticado();
            var ordemServicoId = ProximaOrdemServico();

            var criado = await client.PostAsJsonAsync("/api/orcamento/recalcular",
                NovoEvento(ordemServicoId, Guid.NewGuid(), DateTime.UtcNow));
            var orcamento = await LerOrcamentoAsync(criado);

            var resposta = await client.PutAsJsonAsync($"/api/orcamento/{orcamento.Id}", new
            {
                valorTotalPecas = -10m,
                valorTotalInsumos = 0m,
                valorTotalServicos = 0m
            });

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }

        [Fact]
        public async Task Remover_OrcamentoExistente_RemoveEDepoisNaoEncontra()
        {
            var client = _factory.CriarClienteAutenticado();
            var ordemServicoId = ProximaOrdemServico();

            var criado = await client.PostAsJsonAsync("/api/orcamento/recalcular",
                NovoEvento(ordemServicoId, Guid.NewGuid(), DateTime.UtcNow));
            var orcamento = await LerOrcamentoAsync(criado);

            var remocao = await client.DeleteAsync($"/api/orcamento/{orcamento.Id}");
            Assert.Equal(HttpStatusCode.OK, remocao.StatusCode);

            var consulta = await client.GetAsync($"/api/orcamento/{orcamento.Id}");
            Assert.Equal(HttpStatusCode.NotFound, consulta.StatusCode);
        }

        [Fact]
        public async Task Requisicao_DevolveCorrelationIdRecebido()
        {
            var client = _factory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/health");
            request.Headers.Add("X-Correlation-Id", "abc123");

            var resposta = await client.SendAsync(request);

            Assert.Equal("abc123", resposta.Headers.GetValues("X-Correlation-Id").Single());
        }

        private static async Task<OrcamentoDto> LerOrcamentoAsync(HttpResponseMessage resposta)
        {
            var corpo = await resposta.Content.ReadAsStringAsync();

            // O ResponseDto expõe o conteúdo em getResponse (o campo público do envelope).
            using var documento = JsonDocument.Parse(corpo);
            var payload = documento.RootElement.GetProperty("getResponse").GetRawText();

            return JsonSerializer.Deserialize<OrcamentoDto>(payload, JsonOptions)!;
        }

        private static object NovoEvento(int ordemServicoId, Guid eventoId, DateTime ocorridoEm) => new
        {
            eventoId,
            tipoEvento = "ordem-servico.itens-alterados",
            ocorridoEm,
            correlationId = "teste-integracao",
            ordemServicoId,
            ordemServico = new
            {
                descricaoProblema = "Revisao completa",
                status = "EmDiagnostico",
                clienteNome = "Carlos Oliveira",
                clienteEmail = "carlos@email.com",
                clienteTelefone = "11999990001",
                veiculoMarca = "Toyota",
                veiculoModelo = "Corolla",
                veiculoPlaca = "ABC1D23",
                veiculoAno = 2021
            },
            itens = new object[]
            {
                new { itemId = 1, descricao = "Pastilha de freio", categoria = (int)CategoriaItem.Peca, quantidade = 2, valorUnitario = 100m },
                new { itemId = 2, descricao = "Oleo 5W30", categoria = (int)CategoriaItem.Insumo, quantidade = 2, valorUnitario = 50m },
                new { itemId = 3, descricao = "Mao de obra", categoria = (int)CategoriaItem.Servico, quantidade = 1, valorUnitario = 150m }
            }
        };

        // Cada teste usa uma OS própria: o banco InMemory é compartilhado pela fixture e o
        // índice único de OrdemServicoId não permite dois orçamentos para a mesma OS.
        private static int _proximaOrdemServico = 1000;

        private static int ProximaOrdemServico() => Interlocked.Increment(ref _proximaOrdemServico);
    }
}
