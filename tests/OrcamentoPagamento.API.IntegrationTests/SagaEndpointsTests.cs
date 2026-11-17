using MassTransit;
using MechanicLtda.Contratos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrcamentoPagamento.Infrastructure;
using OrcamentoPagamento.Application.DTOs;
using OrcamentoPagamento.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OrcamentoPagamento.API.IntegrationTests
{
    /// <summary>
    /// API com a mensageria ligada no transporte em memória do MassTransit: os comandos da
    /// Saga passam pelos mesmos consumidores, filas e serialização (JSON puro) que no RabbitMQ.
    /// </summary>
    public class OrcamentoApiFactoryComSaga : OrcamentoApiFactory
    {
        protected override IDictionary<string, string?> ConfiguracaoAdicional() => new Dictionary<string, string?>
        {
            ["Mensageria:Habilitada"] = "true",
            ["Mensageria:Transporte"] = "InMemory"
        };
    }

    public class SagaEndpointsTests : IClassFixture<OrcamentoApiFactoryComSaga>
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private static int _proximaOs = 70_000;

        private readonly OrcamentoApiFactoryComSaga _factory;

        public SagaEndpointsTests(OrcamentoApiFactoryComSaga factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GerarOrcamento_PeloBus_CriaOrcamentoAbertoDaSaga()
        {
            var client = _factory.CriarClienteAutenticado();
            var saga = Guid.NewGuid();
            var os = Interlocked.Increment(ref _proximaOs);

            await PublicarAsync(GerarOrcamento(saga, os));

            var orcamento = await AguardarOrcamentoAsync(client, os, o => o.CorrelationId == saga);

            Assert.Equal(StatusOrcamento.Aberto, orcamento.Status);
            Assert.Equal(299.80m, orcamento.ValorTotalGeral);
            Assert.Equal(179.80m, orcamento.ValorTotalPecas);
            Assert.Equal(120.00m, orcamento.ValorTotalServicos);
            Assert.NotNull(orcamento.Validade);
        }

        [Fact]
        public async Task GerarOrcamento_ComVeiculoETelefone_GravaOsDadosDoPdf()
        {
            var client = _factory.CriarClienteAutenticado();
            var saga = Guid.NewGuid();
            var os = Interlocked.Increment(ref _proximaOs);

            await PublicarAsync(GerarOrcamento(saga, os) with
            {
                TelefoneCliente = "11999990000",
                DescricaoProblema = "Barulho no freio",
                Veiculo = new VeiculoOrdemServico { VeiculoId = 12, Placa = "ABC1D23", Marca = "Volkswagen", Modelo = "Gol", Ano = 2019 }
            });
            var orcamento = await AguardarOrcamentoAsync(client, os, o => o.CorrelationId == saga);

            using (var scope = _factory.Services.CreateScope())
            {
                var banco = scope.ServiceProvider.GetRequiredService<BancoOrcamentoContext>();
                var snapshot = await banco.Orcamentos.AsNoTracking()
                    .Where(o => o.Id == orcamento.Id)
                    .Select(o => o.OrdemServicoSnapshot)
                    .SingleAsync();

                Assert.NotNull(snapshot);
                Assert.Equal("11999990000", snapshot!.ClienteTelefone);
                Assert.Equal("Barulho no freio", snapshot.DescricaoProblema);
                Assert.Equal("ABC1D23", snapshot.VeiculoPlaca);
                Assert.Equal("Volkswagen", snapshot.VeiculoMarca);
                Assert.Equal("Gol", snapshot.VeiculoModelo);
                Assert.Equal(2019, snapshot.VeiculoAno);
            }

            var pdf = await client.GetAsync($"/api/orcamento/{orcamento.Id}/pdf");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task FluxoCompleto_GerarPagarECompensar()
        {
            var client = _factory.CriarClienteAutenticado();
            var saga = Guid.NewGuid();
            var os = Interlocked.Increment(ref _proximaOs);

            await PublicarAsync(GerarOrcamento(saga, os));
            var orcamento = await AguardarOrcamentoAsync(client, os, o => o.CorrelationId == saga);

            // O cliente paga com a OS em Aguardando Aprovação: vira PagamentoConfirmado.
            var pagamento = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamento.Id, "APRO"));
            Assert.Equal(HttpStatusCode.Created, pagamento.StatusCode);
            var pagamentoId = await LerIdAsync(pagamento);

            orcamento = await AguardarOrcamentoAsync(client, os, o => o.Status == StatusOrcamento.Pago);
            Assert.Equal("Pago", orcamento.StatusDescricao);

            // A execução falhou: o OS Service pede o estorno e o cancelamento.
            await PublicarAsync(new EstornarPagamento { CorrelationId = saga, OrdemServicoId = os, DataHora = DateTime.UtcNow, PagamentoId = pagamentoId, Motivo = "ExecucaoFalhou" });
            await PublicarAsync(new CancelarOrcamento { CorrelationId = saga, OrdemServicoId = os, DataHora = DateTime.UtcNow, OrcamentoId = orcamento.Id, Motivo = "ExecucaoFalhou" });

            orcamento = await AguardarOrcamentoAsync(client, os, o => o.Status == StatusOrcamento.Cancelado);
            Assert.Equal("ExecucaoFalhou", orcamento.MotivoStatus);

            var estornado = await AguardarAsync(async () =>
            {
                var resposta = await client.GetAsync($"/api/pagamento/{pagamentoId}");
                var dto = Ler<PagamentoDto>(await resposta.Content.ReadAsStringAsync());
                return dto.Status == StatusPagamento.Devolvido ? dto : null;
            });
            Assert.Equal(StatusPagamento.Devolvido, estornado.Status);
        }

        [Fact]
        public async Task Recusar_EncerraOrcamentoEBloqueiaPagamento()
        {
            var client = _factory.CriarClienteAutenticado();
            var saga = Guid.NewGuid();
            var os = Interlocked.Increment(ref _proximaOs);

            await PublicarAsync(GerarOrcamento(saga, os));
            var orcamento = await AguardarOrcamentoAsync(client, os, o => o.CorrelationId == saga);

            var recusa = await client.PatchAsJsonAsync($"/api/orcamento/{orcamento.Id}/recusar", new { motivo = "Muito caro" });
            Assert.Equal(HttpStatusCode.OK, recusa.StatusCode);
            var recusado = Ler<OrcamentoDto>(await recusa.Content.ReadAsStringAsync());
            Assert.Equal(StatusOrcamento.Recusado, recusado.Status);
            Assert.Equal("Muito caro", recusado.MotivoStatus);

            var pagamento = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamento.Id, "APRO"));
            Assert.Equal(HttpStatusCode.Conflict, pagamento.StatusCode);

            var aprovacao = await client.PatchAsync($"/api/orcamento/{orcamento.Id}/aprovar", null);
            Assert.Equal(HttpStatusCode.Conflict, aprovacao.StatusCode);
        }

        [Fact]
        public async Task Aprovar_OrcamentoAberto_Retorna200EContinuaPagavel()
        {
            var client = _factory.CriarClienteAutenticado();
            var saga = Guid.NewGuid();
            var os = Interlocked.Increment(ref _proximaOs);

            await PublicarAsync(GerarOrcamento(saga, os));
            var orcamento = await AguardarOrcamentoAsync(client, os, o => o.CorrelationId == saga);

            var aprovacao = await client.PatchAsync($"/api/orcamento/{orcamento.Id}/aprovar", null);
            Assert.Equal(HttpStatusCode.OK, aprovacao.StatusCode);
            Assert.Equal(StatusOrcamento.Aprovado, Ler<OrcamentoDto>(await aprovacao.Content.ReadAsStringAsync()).Status);

            var pagamento = await client.PostAsJsonAsync("/api/pagamento", NovoPagamento(orcamento.Id, "APRO"));
            Assert.Equal(HttpStatusCode.Created, pagamento.StatusCode);
        }

        [Fact]
        public async Task Recusar_OrcamentoInexistente_Retorna404()
        {
            var client = _factory.CriarClienteAutenticado();

            var resposta = await client.PatchAsJsonAsync("/api/orcamento/999999/recusar", new { motivo = "x" });

            Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        }

        [Fact]
        public async Task Recusar_ComRoleCliente_Retorna403()
        {
            var client = _factory.CriarClienteAutenticado("Cliente");

            var resposta = await client.PatchAsJsonAsync("/api/orcamento/1/recusar", new { motivo = "x" });

            Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        }

        // ─── Apoio ─────────────────────────────────────────────────────────────

        private async Task PublicarAsync<T>(T mensagem) where T : class
        {
            var bus = _factory.Services.GetRequiredService<IBus>();
            await bus.Publish(mensagem);
        }

        private static GerarOrcamento GerarOrcamento(Guid saga, int os) => new()
        {
            CorrelationId = saga,
            OrdemServicoId = os,
            DataHora = DateTime.UtcNow,
            ClienteId = 7,
            NomeCliente = "Maria Souza",
            EmailCliente = "maria@exemplo.com",
            Itens =
            [
                new ItemOrdemServico { Descricao = "Pastilha de freio dianteira", Tipo = "Peca", Quantidade = 2, ValorUnitario = 89.90m, EstoqueId = 15 },
                new ItemOrdemServico { Descricao = "Troca de pastilhas", Tipo = "Servico", Quantidade = 1, ValorUnitario = 120.00m, ServicoOficinaId = 3 },
            ]
        };

        private static async Task<OrcamentoDto> AguardarOrcamentoAsync(HttpClient client, int os, Func<OrcamentoDto, bool> condicao) =>
            await AguardarAsync(async () =>
            {
                var resposta = await client.GetAsync($"/api/orcamento/ordem-servico/{os}");
                if (resposta.StatusCode != HttpStatusCode.OK)
                    return null;

                var dto = Ler<OrcamentoDto>(await resposta.Content.ReadAsStringAsync());
                return condicao(dto) ? dto : null;
            });

        /// <summary>O consumo é assíncrono: espera até 10s pela condição.</summary>
        private static async Task<T> AguardarAsync<T>(Func<Task<T?>> obter) where T : class
        {
            var limite = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                var valor = await obter();
                if (valor is not null)
                    return valor;

                if (DateTime.UtcNow > limite)
                    throw new TimeoutException("A Saga nao chegou ao estado esperado em 10s.");

                await Task.Delay(100);
            }
        }

        private static T Ler<T>(string corpo)
        {
            using var documento = JsonDocument.Parse(corpo);
            return JsonSerializer.Deserialize<T>(documento.RootElement.GetProperty("getResponse").GetRawText(), JsonOptions)!;
        }

        private static async Task<int> LerIdAsync(HttpResponseMessage resposta)
        {
            using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            return documento.RootElement.GetProperty("getResponse").GetProperty("id").GetInt32();
        }

        private static object NovoPagamento(int orcamentoId, string token) => new
        {
            requisicaoId = Guid.NewGuid(),
            orcamentoId,
            token,
            paymentMethodId = "master",
            installments = 1,
            payer = new
            {
                email = "test_user_comprador@testuser.com",
                identification = new { type = "CPF", number = "12345678909" }
            }
        };
    }
}
