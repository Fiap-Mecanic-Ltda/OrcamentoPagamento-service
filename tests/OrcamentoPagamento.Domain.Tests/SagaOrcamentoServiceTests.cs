using MechanicLtda.Contratos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Enums;
using OrcamentoPagamento.Domain.Exceptions;
using OrcamentoPagamento.Domain.Interfaces.Gateways;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using OrcamentoPagamento.Domain.Interfaces.Repositories;
using OrcamentoPagamento.Domain.Interfaces.Services;
using OrcamentoPagamento.Domain.Services;
using Xunit;

namespace OrcamentoPagamento.Domain.Tests
{
    public class SagaOrcamentoServiceTests
    {
        private static readonly Guid Saga = Guid.Parse("3f0c2f6e-8a4b-4c1e-9a52-0b6f1d2e7c11");
        private const int Os = 42;

        private readonly Mock<IOrcamentoService> _orcamentoService = new();
        private readonly Mock<IOrcamentoRepository> _orcamentoRepositorio = new();
        private readonly Mock<IPagamentoRepository> _pagamentoRepositorio = new();
        private readonly Mock<IPagamentoService> _pagamentoService = new();
        private readonly Mock<IGatewayPagamento> _gateway = new();
        private readonly Mock<IPublicadorSaga> _publicador = new();
        private readonly SagaOrcamentoService _service;

        public SagaOrcamentoServiceTests()
        {
            _orcamentoRepositorio.Setup(r => r.AtualizarAsync(It.IsAny<Orcamento>())).ReturnsAsync((Orcamento o) => o);
            _pagamentoRepositorio.Setup(r => r.AtualizarAsync(It.IsAny<Pagamento>())).ReturnsAsync((Pagamento p) => p);

            _service = new SagaOrcamentoService(
                NullLogger<SagaOrcamentoService>.Instance,
                new NotificadorService(),
                _orcamentoService.Object,
                _orcamentoRepositorio.Object,
                _pagamentoRepositorio.Object,
                _pagamentoService.Object,
                _gateway.Object,
                _publicador.Object);
        }

        // ─── GerarOrcamento ────────────────────────────────────────────────────

        [Fact]
        public async Task GerarOrcamento_ItensValidos_CalculaComTipoTextoEPublicaOrcamentoGerado()
        {
            CalcularOrcamentoCommand? recebido = null;
            _orcamentoService
                .Setup(s => s.CalcularAsync(It.IsAny<CalcularOrcamentoCommand>(), It.IsAny<CancellationToken>()))
                .Callback((CalcularOrcamentoCommand c, CancellationToken _) => recebido = c)
                .ReturnsAsync(new Orcamento { Id = 7, OrdemServicoId = Os, ValorTotalGeral = 299.80m, Validade = new DateTime(2026, 12, 9) });

            await _service.TratarAsync(Comando());

            Assert.NotNull(recebido);
            Assert.Equal(Saga, recebido!.CorrelationId);
            Assert.Equal(Os, recebido.OrdemServicoId);
            Assert.Equal("Maria Souza", recebido.OrdemServico!.ClienteNome);
            Assert.Equal("maria@exemplo.com", recebido.OrdemServico.ClienteEmail);
            Assert.Collection(recebido.Itens,
                i => { Assert.Equal(CategoriaItem.Peca, i.Categoria); Assert.Equal(2, i.Quantidade); Assert.Equal(89.90m, i.ValorUnitario); },
                i => Assert.Equal(CategoriaItem.Servico, i.Categoria));

            _publicador.Verify(p => p.PublicarAsync(
                It.Is<OrcamentoGerado>(m => m.CorrelationId == Saga && m.OrdemServicoId == Os && m.OrcamentoId == 7
                                            && m.ValorTotal == 299.80m && m.Validade == new DateTime(2026, 12, 9)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GerarOrcamento_ComVeiculoETelefone_LevaOsDadosParaOSnapshotDoPdf()
        {
            CalcularOrcamentoCommand? recebido = null;
            _orcamentoService
                .Setup(s => s.CalcularAsync(It.IsAny<CalcularOrcamentoCommand>(), It.IsAny<CancellationToken>()))
                .Callback((CalcularOrcamentoCommand c, CancellationToken _) => recebido = c)
                .ReturnsAsync(new Orcamento { Id = 7, OrdemServicoId = Os, ValorTotalGeral = 299.80m });

            await _service.TratarAsync(Comando() with
            {
                TelefoneCliente = "11999990000",
                DescricaoProblema = "Barulho no freio",
                Veiculo = new VeiculoOrdemServico { VeiculoId = 12, Placa = "ABC1D23", Marca = "Volkswagen", Modelo = "Gol", Ano = 2019 }
            });

            var dados = recebido!.OrdemServico!;
            Assert.Equal("11999990000", dados.ClienteTelefone);
            Assert.Equal("Barulho no freio", dados.DescricaoProblema);
            Assert.Equal("ABC1D23", dados.VeiculoPlaca);
            Assert.Equal("Volkswagen", dados.VeiculoMarca);
            Assert.Equal("Gol", dados.VeiculoModelo);
            Assert.Equal(2019, dados.VeiculoAno);
        }

        [Fact]
        public async Task GerarOrcamento_SemOsCamposOpcionais_GeraMesmoAssim()
        {
            CalcularOrcamentoCommand? recebido = null;
            _orcamentoService
                .Setup(s => s.CalcularAsync(It.IsAny<CalcularOrcamentoCommand>(), It.IsAny<CancellationToken>()))
                .Callback((CalcularOrcamentoCommand c, CancellationToken _) => recebido = c)
                .ReturnsAsync(new Orcamento { Id = 7, OrdemServicoId = Os, ValorTotalGeral = 299.80m });

            await _service.TratarAsync(Comando());

            var dados = recebido!.OrdemServico!;
            Assert.Null(dados.ClienteTelefone);
            Assert.Null(dados.VeiculoPlaca);
            Assert.Null(dados.VeiculoAno);
            _publicador.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoGerado>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GerarOrcamento_RepetidoDaMesmaSaga_RepublicaSemRecalcular()
        {
            ComOrcamentoDaOs(new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Saga, ValorTotalGeral = 299.80m });

            await _service.TratarAsync(Comando());

            _orcamentoService.Verify(s => s.CalcularAsync(It.IsAny<CalcularOrcamentoCommand>(), It.IsAny<CancellationToken>()), Times.Never);
            _publicador.Verify(p => p.PublicarAsync(It.Is<OrcamentoGerado>(m => m.OrcamentoId == 7), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GerarOrcamento_RepetidoDepoisDeCancelado_Ignora()
        {
            ComOrcamentoDaOs(new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Saga, Status = StatusOrcamento.Cancelado });

            await _service.TratarAsync(Comando());

            _orcamentoService.Verify(s => s.CalcularAsync(It.IsAny<CalcularOrcamentoCommand>(), It.IsAny<CancellationToken>()), Times.Never);
            _publicador.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GerarOrcamento_SagaNovaParaOsComOrcamentoAntigo_Recalcula()
        {
            ComOrcamentoDaOs(new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Guid.NewGuid(), Status = StatusOrcamento.Recusado });
            CalcularAsyncDevolve(new Orcamento { Id = 7, OrdemServicoId = Os, ValorTotalGeral = 299.80m });

            await _service.TratarAsync(Comando());

            _orcamentoService.Verify(s => s.CalcularAsync(It.Is<CalcularOrcamentoCommand>(c => c.CorrelationId == Saga), It.IsAny<CancellationToken>()), Times.Once);
            _publicador.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoGerado>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData("Motor", "Tipo de item invalido")]
        [InlineData("1", "Tipo de item invalido")]
        [InlineData("", "Tipo de item invalido")]
        public async Task GerarOrcamento_TipoInvalido_PublicaOrcamentoFalhou(string tipo, string motivo)
        {
            await _service.TratarAsync(Comando(new ItemOrdemServico { Descricao = "x", Tipo = tipo, Quantidade = 1, ValorUnitario = 10m }));

            VerificaFalha(motivo);
        }

        [Fact]
        public async Task GerarOrcamento_SemItens_PublicaOrcamentoFalhou()
        {
            await _service.TratarAsync(ComandoComItens([]));

            VerificaFalha("nao trouxe itens");
        }

        [Fact]
        public async Task GerarOrcamento_ValorZerado_PublicaOrcamentoFalhou()
        {
            await _service.TratarAsync(Comando(new ItemOrdemServico { Descricao = "Cortesia", Tipo = "Servico", Quantidade = 1, ValorUnitario = 0m }));

            VerificaFalha("valor zerado");
        }

        [Fact]
        public async Task GerarOrcamento_QuantidadeInvalida_PublicaOrcamentoFalhou()
        {
            await _service.TratarAsync(Comando(new ItemOrdemServico { Descricao = "Pastilha", Tipo = "Peca", Quantidade = 0, ValorUnitario = 10m }));

            VerificaFalha("Quantidade invalida");
        }

        [Fact]
        public async Task GerarOrcamento_FalhaTecnicaNoCalculo_Propaga()
        {
            _orcamentoService
                .Setup(s => s.CalcularAsync(It.IsAny<CalcularOrcamentoCommand>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("banco fora"));

            await Assert.ThrowsAsync<TimeoutException>(() => _service.TratarAsync(Comando()));

            _publicador.VerifyNoOtherCalls();
        }

        // ─── CancelarOrcamento ─────────────────────────────────────────────────

        [Fact]
        public async Task CancelarOrcamento_Aberto_CancelaPagamentosEmAbertoEConfirma()
        {
            var orcamento = new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Saga };
            ComOrcamentoDaOs(orcamento);

            await _service.TratarAsync(new CancelarOrcamento { CorrelationId = Saga, OrdemServicoId = Os, OrcamentoId = 7, Motivo = "Prazo vencido" });

            Assert.Equal(StatusOrcamento.Cancelado, orcamento.Status);
            Assert.Equal("Prazo vencido", orcamento.MotivoStatus);
            _pagamentoService.Verify(s => s.CancelarEmAbertoAsync(7, MotivoCancelamento.CompensacaoDaSaga, It.IsAny<CancellationToken>()), Times.Once);
            _publicador.Verify(p => p.PublicarAsync(
                It.Is<OrcamentoCancelado>(m => m.OrcamentoId == 7 && m.CorrelationId == Saga), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CancelarOrcamento_JaCancelado_SoConfirmaDeNovo()
        {
            ComOrcamentoDaOs(new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Saga, Status = StatusOrcamento.Cancelado });

            await _service.TratarAsync(new CancelarOrcamento { CorrelationId = Saga, OrdemServicoId = Os, OrcamentoId = 7 });

            _orcamentoRepositorio.Verify(r => r.AtualizarAsync(It.IsAny<Orcamento>()), Times.Never);
            _publicador.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoCancelado>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CancelarOrcamento_Inexistente_ConfirmaParaACompensacaoTerminar()
        {
            await _service.TratarAsync(new CancelarOrcamento { CorrelationId = Saga, OrdemServicoId = Os, OrcamentoId = 99 });

            _publicador.Verify(p => p.PublicarAsync(It.Is<OrcamentoCancelado>(m => m.OrcamentoId == 99), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CancelarOrcamento_DeSagaAntiga_NaoMexeNoOrcamentoReaberto()
        {
            var orcamento = new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Guid.NewGuid() };
            ComOrcamentoDaOs(orcamento);

            await _service.TratarAsync(new CancelarOrcamento { CorrelationId = Saga, OrdemServicoId = Os, OrcamentoId = 7 });

            Assert.Equal(StatusOrcamento.Aberto, orcamento.Status);
            _publicador.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoCancelado>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // ─── EstornarPagamento ─────────────────────────────────────────────────

        [Fact]
        public async Task EstornarPagamento_Aprovado_DevolveNoProvedorEConfirma()
        {
            var pagamento = ComPagamento(StatusPagamento.Aprovado);
            Guid chave = Guid.Empty;
            _gateway
                .Setup(g => g.EstornarAsync("mp-9", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Callback((string _, Guid c, CancellationToken _) => chave = c)
                .ReturnsAsync(new RespostaGatewayPagamento { IdExterno = "mp-9", Status = StatusPagamento.Devolvido, StatusDetalhe = "refunded" });

            await _service.TratarAsync(Estornar());

            Assert.Equal(StatusPagamento.Devolvido, pagamento.Status);
            // Chave estável por Saga e pagamento: uma reentrega não devolve duas vezes no provedor.
            Assert.Equal(SagaOrcamentoService.ChaveDeterministica($"EstornarPagamento#{Saga}#9"), chave);
            Assert.NotEqual(Guid.Empty, chave);
            _pagamentoRepositorio.Verify(r => r.AtualizarAsync(pagamento), Times.Once);
            _publicador.Verify(p => p.PublicarAsync(It.Is<PagamentoEstornado>(m => m.PagamentoId == 9 && m.CorrelationId == Saga), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EstornarPagamento_ProvedorAindaMostraAprovado_RegistraEstorno()
        {
            var pagamento = ComPagamento(StatusPagamento.Aprovado);
            _gateway
                .Setup(g => g.EstornarAsync("mp-9", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RespostaGatewayPagamento { IdExterno = "mp-9", Status = StatusPagamento.Aprovado, StatusDetalhe = "accredited" });

            await _service.TratarAsync(Estornar());

            Assert.Equal(StatusPagamento.Estornado, pagamento.Status);
        }

        [Fact]
        public async Task EstornarPagamento_Pendente_CancelaSemChamarOProvedor()
        {
            var pagamento = ComPagamento(StatusPagamento.Pendente);

            await _service.TratarAsync(Estornar());

            Assert.Equal(StatusPagamento.Cancelado, pagamento.Status);
            _gateway.Verify(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
            _publicador.Verify(p => p.PublicarAsync(It.IsAny<PagamentoEstornado>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(StatusPagamento.Recusado)]
        [InlineData(StatusPagamento.Devolvido)]
        [InlineData(StatusPagamento.Estornado)]
        public async Task EstornarPagamento_SemValorADevolver_SoConfirma(StatusPagamento status)
        {
            ComPagamento(status);

            await _service.TratarAsync(Estornar());

            _gateway.Verify(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
            _publicador.Verify(p => p.PublicarAsync(It.IsAny<PagamentoEstornado>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EstornarPagamento_ProvedorFora_PropagaParaORetry()
        {
            ComPagamento(StatusPagamento.Aprovado);
            _gateway
                .Setup(g => g.EstornarAsync("mp-9", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new GatewayPagamentoException("Mercado Pago fora"));

            await Assert.ThrowsAsync<GatewayPagamentoException>(() => _service.TratarAsync(Estornar()));

            _publicador.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task EstornarPagamento_DeOutraOs_SoConfirma()
        {
            var pagamento = ComPagamento(StatusPagamento.Aprovado);
            pagamento.OrdemServicoId = 1;

            await _service.TratarAsync(Estornar());

            Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
            _publicador.Verify(p => p.PublicarAsync(It.IsAny<PagamentoEstornado>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // ─── Aprovação e recusa ────────────────────────────────────────────────

        [Fact]
        public async Task Aprovar_OrcamentoAberto_MarcaAprovadoEPublicaOrcamentoAprovado()
        {
            var orcamento = ComOrcamento(new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Saga });

            await _service.AprovarAsync(7);

            Assert.Equal(StatusOrcamento.Aprovado, orcamento.Status);
            _publicador.Verify(p => p.PublicarAsync(
                It.Is<OrcamentoAprovado>(m => m.OrcamentoId == 7 && m.CorrelationId == Saga && m.OrdemServicoId == Os), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Aprovar_OrcamentoCancelado_LancaIndisponivel()
        {
            ComOrcamento(new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Saga, Status = StatusOrcamento.Cancelado });

            await Assert.ThrowsAsync<OrcamentoIndisponivelException>(() => _service.AprovarAsync(7));
        }

        [Fact]
        public async Task Recusar_OrcamentoAberto_EncerraCancelaPagamentosEPublica()
        {
            var orcamento = ComOrcamento(new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Saga });

            await _service.RecusarAsync(7, "Muito caro");

            Assert.Equal(StatusOrcamento.Recusado, orcamento.Status);
            _pagamentoService.Verify(s => s.CancelarEmAbertoAsync(7, MotivoCancelamento.OrcamentoRecusado, It.IsAny<CancellationToken>()), Times.Once);
            _publicador.Verify(p => p.PublicarAsync(
                It.Is<OrcamentoRecusado>(m => m.OrcamentoId == 7 && m.Motivo == "Muito caro"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Recusar_JaRecusado_RepublicaSemMexer()
        {
            ComOrcamento(new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Saga, Status = StatusOrcamento.Recusado });

            await _service.RecusarAsync(7, null);

            _orcamentoRepositorio.Verify(r => r.AtualizarAsync(It.IsAny<Orcamento>()), Times.Never);
            _publicador.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoRecusado>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Recusar_OrcamentoPago_LancaIndisponivel()
        {
            ComOrcamento(new Orcamento { Id = 7, OrdemServicoId = Os, CorrelationId = Saga, Status = StatusOrcamento.Pago });

            await Assert.ThrowsAsync<OrcamentoIndisponivelException>(() => _service.RecusarAsync(7, null));
        }

        [Fact]
        public async Task Recusar_OrcamentoForaDaSaga_NaoPublica()
        {
            ComOrcamento(new Orcamento { Id = 7, OrdemServicoId = Os });

            await _service.RecusarAsync(7, null);

            _publicador.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Recusar_Inexistente_LancaKeyNotFound()
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.RecusarAsync(7, null));
        }

        // ─── Apoio ─────────────────────────────────────────────────────────────

        private static GerarOrcamento Comando(params ItemOrdemServico[] itens) => new()
        {
            CorrelationId = Saga,
            OrdemServicoId = Os,
            DataHora = new DateTime(2026, 11, 9, 14, 3, 12, DateTimeKind.Utc),
            ClienteId = 7,
            NomeCliente = "Maria Souza",
            EmailCliente = "maria@exemplo.com",
            Itens = itens.Length > 0 ? itens :
            [
                new ItemOrdemServico { Descricao = "Pastilha de freio dianteira", Tipo = "Peca", Quantidade = 2, ValorUnitario = 89.90m, EstoqueId = 15 },
                new ItemOrdemServico { Descricao = "Troca de pastilhas", Tipo = "Servico", Quantidade = 1, ValorUnitario = 120.00m, ServicoOficinaId = 3 },
            ]
        };

        private static GerarOrcamento ComandoComItens(IReadOnlyList<ItemOrdemServico> itens) => Comando() with { Itens = itens };

        private static EstornarPagamento Estornar() => new() { CorrelationId = Saga, OrdemServicoId = Os, PagamentoId = 9, Motivo = "ExecucaoFalhou" };

        private void CalcularAsyncDevolve(Orcamento orcamento) =>
            _orcamentoService
                .Setup(s => s.CalcularAsync(It.IsAny<CalcularOrcamentoCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(orcamento);

        private void ComOrcamentoDaOs(Orcamento orcamento) =>
            _orcamentoRepositorio.Setup(r => r.ObterPorOrdemServicoIdAsync(orcamento.OrdemServicoId)).ReturnsAsync(orcamento);

        private Orcamento ComOrcamento(Orcamento orcamento)
        {
            _orcamentoRepositorio.Setup(r => r.ObterPorIdAsync(orcamento.Id.ToString())).ReturnsAsync(orcamento);
            return orcamento;
        }

        private Pagamento ComPagamento(StatusPagamento status)
        {
            var pagamento = new Pagamento { Id = 9, OrcamentoId = 7, OrdemServicoId = Os, Valor = 299.80m, Status = status, IdExterno = "mp-9" };
            _pagamentoRepositorio.Setup(r => r.ObterPorIdAsync("9")).ReturnsAsync(pagamento);
            return pagamento;
        }

        private void VerificaFalha(string trechoDoMotivo)
        {
            _orcamentoService.Verify(s => s.CalcularAsync(It.IsAny<CalcularOrcamentoCommand>(), It.IsAny<CancellationToken>()), Times.Never);
            _publicador.Verify(p => p.PublicarAsync(
                It.Is<OrcamentoFalhou>(m => m.CorrelationId == Saga && m.OrdemServicoId == Os && m.Motivo.Contains(trechoDoMotivo)),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
