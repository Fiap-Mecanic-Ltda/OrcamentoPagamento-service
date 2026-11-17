using OrcamentoPagamento.Application.DTOs;
using OrcamentoPagamento.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OrcamentoPagamento.API.IntegrationTests
{
    /// <summary>
    /// Roda com o gateway fake (Pagamentos:Habilitado = false), que decide o resultado pelo
    /// token usando os mesmos códigos do Mercado Pago: APRO aprova, FUND recusa, CONT deixa
    /// pendente. A CI não depende do sandbox do provedor.
    /// </summary>
    public class PagamentoEndpointsTests : IClassFixture<OrcamentoApiFactory>
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly OrcamentoApiFactory _factory;

        public PagamentoEndpointsTests(OrcamentoApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Criar_SemToken_Retorna401()
        {
            var client = _factory.CreateClient();

            var resposta = await client.PostAsJsonAsync("/api/pagamento", new { orcamentoId = 1 });

            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        }

        [Fact]
        public async Task Criar_ComRoleCliente_Retorna403()
        {
            var client = _factory.CriarClienteAutenticado("Cliente");

            var resposta = await client.PostAsJsonAsync("/api/pagamento", new { orcamentoId = 1 });

            Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        }

        [Fact]
        public async Task Criar_OrcamentoInexistente_Retorna404()
        {
            var client = _factory.CriarClienteAutenticado();

            var resposta = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(999999, "APRO"));

            Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        }

        [Fact]
        public async Task Criar_PayloadInvalido_Retorna400()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);

            // Sem token e sem paymentMethodId: barrado na validação do ViewModel.
            var resposta = await client.PostAsJsonAsync("/api/pagamento", new
            {
                orcamentoId,
                payer = new { email = "comprador@testuser.com" }
            });

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }

        [Fact]
        public async Task Criar_TokenAPRO_Retorna201ComValorDoOrcamento()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);

            var resposta = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO"));

            Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

            var pagamento = await LerPagamentoAsync(resposta);

            Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
            Assert.Equal("Aprovado", pagamento.StatusDescricao);

            // O valor é o do orçamento (450), e não qualquer coisa que o cliente mandasse.
            Assert.Equal(450m, pagamento.Valor);
            Assert.True(pagamento.ModoTeste);
            Assert.NotNull(pagamento.IdExterno);
        }

        [Fact]
        public async Task Criar_TokenFUND_Retorna201ComStatusRecusado()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);

            var resposta = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "FUND"));

            // Recusa é resultado de negócio: 201 com status Recusado, não 400.
            Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

            var pagamento = await LerPagamentoAsync(resposta);

            Assert.Equal(StatusPagamento.Recusado, pagamento.Status);
            Assert.Equal("cc_rejected_insufficient_amount", pagamento.StatusDetalhe);
        }

        [Fact]
        public async Task Criar_MesmoRequisicaoIdDuasVezes_Retorna200ComOMesmoPagamento()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);
            var requisicaoId = Guid.NewGuid();

            var primeira = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO", requisicaoId));
            Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
            var criado = await LerPagamentoAsync(primeira);

            var segunda = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO", requisicaoId));

            Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
            Assert.Equal(criado.Id, (await LerPagamentoAsync(segunda)).Id);

            // E só existe um pagamento no histórico do orçamento.
            var historico = await client.GetAsync($"/api/pagamento/orcamento/{orcamentoId}");
            Assert.Single(await LerListaAsync(historico));
        }

        [Fact]
        public async Task Criar_OrcamentoJaPago_Retorna409()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);

            var primeira = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO"));
            Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);

            var segunda = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO"));

            Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        }

        [Fact]
        public async Task Criar_OrcamentoComValorZerado_Retorna400()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client, semItens: true);

            var resposta = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO"));

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }

        [Fact]
        public async Task Sincronizar_PagamentoPendente_AvancaParaAprovado()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);

            var criado = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "CONT"));
            var pagamento = await LerPagamentoAsync(criado);
            Assert.Equal(StatusPagamento.Pendente, pagamento.Status);

            // É o caminho usado em desenvolvimento, onde localhost não recebe webhook.
            var sincronizacao = await client.PostAsync($"/api/pagamento/{pagamento.Id}/sincronizar", null);

            Assert.Equal(HttpStatusCode.OK, sincronizacao.StatusCode);
            Assert.Equal(StatusPagamento.Aprovado, (await LerPagamentoAsync(sincronizacao)).Status);
        }

        [Fact]
        public async Task Sincronizar_PagamentoJaAprovado_Retorna204()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);

            var criado = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO"));
            var pagamento = await LerPagamentoAsync(criado);

            var sincronizacao = await client.PostAsync($"/api/pagamento/{pagamento.Id}/sincronizar", null);

            Assert.Equal(HttpStatusCode.NoContent, sincronizacao.StatusCode);
        }

        [Fact]
        public async Task Sincronizar_PagamentoInexistente_Retorna404()
        {
            var client = _factory.CriarClienteAutenticado();

            var resposta = await client.PostAsync("/api/pagamento/999999/sincronizar", null);

            Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        }

        [Fact]
        public async Task ObterPorOrdemServico_DevolveOsPagamentosDaOS()
        {
            var client = _factory.CriarClienteAutenticado();
            var ordemServicoId = ProximaOrdemServico();
            var orcamentoId = await CriarOrcamentoAsync(client, ordemServicoId: ordemServicoId);

            await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO"));

            var resposta = await client.GetAsync($"/api/pagamento/ordem-servico/{ordemServicoId}");

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var pagamentos = await LerListaAsync(resposta);
            Assert.Single(pagamentos);
            Assert.Equal(ordemServicoId, pagamentos[0].OrdemServicoId);
        }

        [Fact]
        public async Task ObterPorId_PagamentoInexistente_Retorna404()
        {
            var client = _factory.CriarClienteAutenticado();

            var resposta = await client.GetAsync("/api/pagamento/999999");

            Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        }

        // ─── A regra cruzada: recálculo invalida pagamento pendente ─────────────

        [Fact]
        public async Task RecalcularOrcamento_ComValorDiferente_CancelaPagamentoPendente()
        {
            var client = _factory.CriarClienteAutenticado();
            var ordemServicoId = ProximaOrdemServico();
            var orcamentoId = await CriarOrcamentoAsync(client, ordemServicoId: ordemServicoId);

            var criado = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "CONT"));
            var pagamento = await LerPagamentoAsync(criado);
            Assert.Equal(StatusPagamento.Pendente, pagamento.Status);

            // Novo evento de itens com valor diferente: o pendente deixa de valer.
            await client.PostAsJsonAsync("/api/orcamento/recalcular", new
            {
                eventoId = Guid.NewGuid(),
                tipoEvento = "ordem-servico.itens-alterados",
                ocorridoEm = DateTime.UtcNow,
                ordemServicoId,
                itens = new object[]
                {
                    new { itemId = 1, descricao = "Correia dentada", categoria = (int)CategoriaItem.Peca, quantidade = 1, valorUnitario = 680m }
                }
            });

            var depois = await client.GetAsync($"/api/pagamento/{pagamento.Id}");
            var atualizado = await LerPagamentoAsync(depois);

            Assert.Equal(StatusPagamento.Cancelado, atualizado.Status);
            Assert.Equal("ValorDoOrcamentoAlterado", atualizado.MotivoCancelamento);
        }

        [Fact]
        public async Task RecalcularOrcamento_ComValorDiferente_NaoTocaPagamentoAprovado()
        {
            var client = _factory.CriarClienteAutenticado();
            var ordemServicoId = ProximaOrdemServico();
            var orcamentoId = await CriarOrcamentoAsync(client, ordemServicoId: ordemServicoId);

            var criado = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO"));
            var pagamento = await LerPagamentoAsync(criado);
            Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);

            await client.PostAsJsonAsync("/api/orcamento/recalcular", new
            {
                eventoId = Guid.NewGuid(),
                tipoEvento = "ordem-servico.itens-alterados",
                ocorridoEm = DateTime.UtcNow,
                ordemServicoId,
                itens = new object[]
                {
                    new { itemId = 1, descricao = "Correia dentada", categoria = (int)CategoriaItem.Peca, quantidade = 1, valorUnitario = 680m }
                }
            });

            var depois = await client.GetAsync($"/api/pagamento/{pagamento.Id}");

            // Dinheiro capturado não se cancela: a divergência fica registrada.
            Assert.Equal(StatusPagamento.Aprovado, (await LerPagamentoAsync(depois)).Status);
        }

        [Fact]
        public async Task AjusteManualDoOrcamento_CancelaPagamentoPendente()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);

            var criado = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "CONT"));
            var pagamento = await LerPagamentoAsync(criado);

            await client.PutAsJsonAsync($"/api/orcamento/{orcamentoId}", new
            {
                valorTotalPecas = 900m,
                valorTotalInsumos = 0m,
                valorTotalServicos = 0m
            });

            var depois = await client.GetAsync($"/api/pagamento/{pagamento.Id}");

            Assert.Equal(StatusPagamento.Cancelado, (await LerPagamentoAsync(depois)).Status);
        }

        // ─── Webhook ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Webhook_NaoExigeAutenticacao()
        {
            var client = _factory.CreateClient();

            var resposta = await client.PostAsJsonAsync("/api/webhooks/mercadopago", new
            {
                type = "payment",
                action = "payment.updated",
                data = new { id = "desconhecido-123" }
            });

            // Rota pública, e id desconhecido responde 200 — devolver erro só faria o
            // provedor reenviar para sempre.
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }

        [Fact]
        public async Task Webhook_TipoDiferenteDePayment_Retorna200SemFazerNada()
        {
            var client = _factory.CreateClient();

            var resposta = await client.PostAsJsonAsync("/api/webhooks/mercadopago", new
            {
                type = "subscription_preapproval",
                data = new { id = "123" }
            });

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }

        [Fact]
        public async Task Webhook_DePagamentoPendente_AvancaOStatus()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);

            var criado = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "CONT"));
            var pagamento = await LerPagamentoAsync(criado);
            Assert.Equal(StatusPagamento.Pendente, pagamento.Status);

            var publico = _factory.CreateClient();
            var notificacao = await publico.PostAsJsonAsync("/api/webhooks/mercadopago", new
            {
                type = "payment",
                action = "payment.updated",
                live_mode = false,
                data = new { id = pagamento.IdExterno }
            });

            Assert.Equal(HttpStatusCode.OK, notificacao.StatusCode);

            var depois = await client.GetAsync($"/api/pagamento/{pagamento.Id}");
            Assert.Equal(StatusPagamento.Aprovado, (await LerPagamentoAsync(depois)).Status);
        }

        [Fact]
        public async Task Webhook_NotificacaoRepetida_Retorna200SemRegredir()
        {
            var client = _factory.CriarClienteAutenticado();
            var orcamentoId = await CriarOrcamentoAsync(client);

            var criado = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamentoId, "APRO"));
            var pagamento = await LerPagamentoAsync(criado);

            var publico = _factory.CreateClient();
            var corpo = new
            {
                type = "payment",
                action = "payment.updated",
                data = new { id = pagamento.IdExterno }
            };

            var primeira = await publico.PostAsJsonAsync("/api/webhooks/mercadopago", corpo);
            var segunda = await publico.PostAsJsonAsync("/api/webhooks/mercadopago", corpo);

            Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
            Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);

            var depois = await client.GetAsync($"/api/pagamento/{pagamento.Id}");
            Assert.Equal(StatusPagamento.Aprovado, (await LerPagamentoAsync(depois)).Status);
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Cria um orçamento pelo mesmo caminho que o evento de itens usaria. Total 450
        /// (200 peças + 100 insumos + 150 serviços), ou zero quando semItens.
        /// </summary>
        private static async Task<int> CriarOrcamentoAsync(HttpClient client, int? ordemServicoId = null, bool semItens = false)
        {
            var osId = ordemServicoId ?? ProximaOrdemServico();

            var itens = semItens
                ? Array.Empty<object>()
                : new object[]
                {
                    new { itemId = 1, descricao = "Pastilha de freio", categoria = (int)CategoriaItem.Peca, quantidade = 2, valorUnitario = 100m },
                    new { itemId = 2, descricao = "Oleo 5W30", categoria = (int)CategoriaItem.Insumo, quantidade = 2, valorUnitario = 50m },
                    new { itemId = 3, descricao = "Mao de obra", categoria = (int)CategoriaItem.Servico, quantidade = 1, valorUnitario = 150m }
                };

            var resposta = await client.PostAsJsonAsync("/api/orcamento/recalcular", new
            {
                eventoId = Guid.NewGuid(),
                tipoEvento = "ordem-servico.itens-alterados",
                ocorridoEm = DateTime.UtcNow,
                ordemServicoId = osId,
                ordemServico = new
                {
                    descricaoProblema = "Revisao completa",
                    status = "AguardandoAprovacao",
                    clienteNome = "Carlos Oliveira",
                    clienteEmail = "carlos@email.com",
                    veiculoPlaca = "ABC1D23",
                    veiculoAno = 2021
                },
                itens
            });

            resposta.EnsureSuccessStatusCode();

            var corpo = await resposta.Content.ReadAsStringAsync();
            using var documento = JsonDocument.Parse(corpo);
            return documento.RootElement.GetProperty("getResponse").GetProperty("id").GetInt32();
        }

        private static object NovoPagamento(int orcamentoId, string token, Guid? requisicaoId = null) => new
        {
            requisicaoId = requisicaoId ?? Guid.NewGuid(),
            orcamentoId,
            token,
            paymentMethodId = "master",
            installments = 1,
            descricao = "Pagamento de teste",
            payer = new
            {
                email = "test_user_comprador@testuser.com",
                identification = new { type = "CPF", number = "12345678909" }
            }
        };

        private static async Task<PagamentoDto> LerPagamentoAsync(HttpResponseMessage resposta)
        {
            var corpo = await resposta.Content.ReadAsStringAsync();
            using var documento = JsonDocument.Parse(corpo);
            var payload = documento.RootElement.GetProperty("getResponse").GetRawText();
            return JsonSerializer.Deserialize<PagamentoDto>(payload, JsonOptions)!;
        }

        private static async Task<List<PagamentoDto>> LerListaAsync(HttpResponseMessage resposta)
        {
            var corpo = await resposta.Content.ReadAsStringAsync();
            using var documento = JsonDocument.Parse(corpo);
            var payload = documento.RootElement.GetProperty("getResponse").GetRawText();
            return JsonSerializer.Deserialize<List<PagamentoDto>>(payload, JsonOptions)!;
        }

        // Cada teste usa uma OS própria: o banco InMemory é compartilhado pela fixture e o
        // índice único de OrdemServicoId não permite dois orçamentos para a mesma OS.
        private static int _proximaOrdemServico = 5000;

        private static int ProximaOrdemServico() => Interlocked.Increment(ref _proximaOrdemServico);
    }
}
