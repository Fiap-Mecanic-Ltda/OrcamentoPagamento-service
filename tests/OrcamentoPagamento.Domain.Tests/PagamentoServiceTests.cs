using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Enums;
using OrcamentoPagamento.Domain.Exceptions;
using OrcamentoPagamento.Domain.Interfaces.Gateways;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using OrcamentoPagamento.Domain.Interfaces.Repositories;
using OrcamentoPagamento.Domain.Services;
using Xunit;

namespace OrcamentoPagamento.Domain.Tests
{
    public class PagamentoServiceTests
    {
        private readonly Mock<IPagamentoRepository> _pagamentoRepositorio = new();
        private readonly Mock<IOrcamentoRepository> _orcamentoRepositorio = new();
        private readonly Mock<IGatewayPagamento> _gateway = new();
        private readonly Mock<IPublicadorEventos> _publicador = new();
        private readonly PagamentoService _service;

        public PagamentoServiceTests()
        {
            _gateway.SetupGet(g => g.Nome).Returns(Pagamento.ProvedorMercadoPago);

            _pagamentoRepositorio
                .Setup(r => r.AdicionarAsync(It.IsAny<Pagamento>()))
                .ReturnsAsync((Pagamento p) => p);

            _pagamentoRepositorio
                .Setup(r => r.AtualizarAsync(It.IsAny<Pagamento>()))
                .ReturnsAsync((Pagamento p) => p);

            _service = new PagamentoService(
                NullLogger<PagamentoService>.Instance,
                new NotificadorService(),
                _pagamentoRepositorio.Object,
                _orcamentoRepositorio.Object,
                _gateway.Object,
                _publicador.Object);
        }

        // ─── Criação ────────────────────────────────────────────────────────────

        [Fact]
        public async Task CriarAsync_UsaOValorDoOrcamentoEIgnoraQualquerValorDeEntrada()
        {
            ComOrcamento(valor: 450m);
            ComGatewayRespondendo(StatusPagamento.Aprovado, "accredited");

            var resultado = await _service.CriarAsync(NovoComando());

            Assert.Equal(450m, resultado.Pagamento.Valor);

            // A prova de que o valor não vem do cliente: o gateway recebeu o do orçamento.
            _gateway.Verify(g => g.CriarAsync(
                It.Is<RequisicaoGatewayPagamento>(r => r.Valor == 450m),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CriarAsync_CongelaOrdemServicoIdEValorDoOrcamento()
        {
            ComOrcamento(valor: 320m, ordemServicoId: 77);
            ComGatewayRespondendo(StatusPagamento.Aprovado, "accredited");

            var resultado = await _service.CriarAsync(NovoComando());

            Assert.Equal(77, resultado.Pagamento.OrdemServicoId);
            Assert.Equal(320m, resultado.Pagamento.Valor);
        }

        [Fact]
        public async Task CriarAsync_MesmaRequisicaoIdDuasVezes_NaoCobraDeNovo()
        {
            var requisicaoId = Guid.NewGuid();
            var jaExistente = new Pagamento { Id = 9, RequisicaoId = requisicaoId, OrcamentoId = 1, Valor = 450m };

            _pagamentoRepositorio
                .Setup(r => r.ObterPorRequisicaoIdAsync(requisicaoId))
                .ReturnsAsync(jaExistente);

            var command = NovoComando();
            command.RequisicaoId = requisicaoId;

            var resultado = await _service.CriarAsync(command);

            Assert.True(resultado.JaExistia);
            Assert.Equal(9, resultado.Pagamento.Id);
            _gateway.Verify(g => g.CriarAsync(It.IsAny<RequisicaoGatewayPagamento>(), It.IsAny<CancellationToken>()), Times.Never);
            _pagamentoRepositorio.Verify(r => r.AdicionarAsync(It.IsAny<Pagamento>()), Times.Never);
        }

        [Fact]
        public async Task CriarAsync_OrcamentoJaPago_LancaPagamentoJaAprovado()
        {
            ComOrcamento(valor: 450m);

            _pagamentoRepositorio
                .Setup(r => r.ObterAprovadoPorOrcamentoIdAsync(1))
                .ReturnsAsync(new Pagamento { Id = 5, OrcamentoId = 1, Status = StatusPagamento.Aprovado });

            await Assert.ThrowsAsync<PagamentoJaAprovadoException>(() => _service.CriarAsync(NovoComando()));

            _gateway.Verify(g => g.CriarAsync(It.IsAny<RequisicaoGatewayPagamento>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CriarAsync_OrcamentoInexistente_LancaKeyNotFound()
        {
            _orcamentoRepositorio
                .Setup(r => r.ObterPorIdAsync("1"))
                .ReturnsAsync((Orcamento?)null);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CriarAsync(NovoComando()));
        }

        [Fact]
        public async Task CriarAsync_OrcamentoComValorZerado_LancaArgumentException()
        {
            ComOrcamento(valor: 0m);

            await Assert.ThrowsAsync<ArgumentException>(() => _service.CriarAsync(NovoComando()));
        }

        [Theory]
        [InlineData("", "master", "pagador@email.com")]
        [InlineData("tok_123", "", "pagador@email.com")]
        [InlineData("tok_123", "master", "")]
        public async Task CriarAsync_ComCampoObrigatorioFaltando_LancaArgumentException(string token, string metodo, string email)
        {
            var command = NovoComando();
            command.Token = token;
            command.MetodoPagamento = metodo;
            command.PagadorEmail = email;

            await Assert.ThrowsAsync<ArgumentException>(() => _service.CriarAsync(command));
        }

        [Fact]
        public async Task CriarAsync_RecusadoPeloProvedor_GravaERetornaNormalmente()
        {
            ComOrcamento(valor: 450m);
            ComGatewayRespondendo(StatusPagamento.Recusado, "cc_rejected_insufficient_amount");

            var resultado = await _service.CriarAsync(NovoComando());

            // Recusa é resultado de negócio: grava, publica e devolve sem exceção.
            Assert.Equal(StatusPagamento.Recusado, resultado.Pagamento.Status);
            Assert.Equal("cc_rejected_insufficient_amount", resultado.Pagamento.StatusDetalhe);
            _pagamentoRepositorio.Verify(r => r.AdicionarAsync(It.IsAny<Pagamento>()), Times.Once);
            VerificaPublicacao(PagamentoService.EventoPagamentoRecusado);
        }

        [Fact]
        public async Task CriarAsync_Aprovado_PublicaCriadoEAprovado()
        {
            ComOrcamento(valor: 450m);
            ComGatewayRespondendo(StatusPagamento.Aprovado, "accredited");

            await _service.CriarAsync(NovoComando());

            VerificaPublicacao(PagamentoService.EventoPagamentoCriado);
            VerificaPublicacao(PagamentoService.EventoPagamentoAprovado);
        }

        [Fact]
        public async Task CriarAsync_Pendente_NaoPublicaEventoDeDesfecho()
        {
            ComOrcamento(valor: 450m);
            ComGatewayRespondendo(StatusPagamento.Pendente, "pending_contingency");

            await _service.CriarAsync(NovoComando());

            VerificaPublicacao(PagamentoService.EventoPagamentoCriado);
            _publicador.Verify(p => p.PublicarAsync(
                PagamentoService.EventoPagamentoAprovado,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CriarAsync_FalhaDoGateway_PropagaGatewayPagamentoException()
        {
            ComOrcamento(valor: 450m);

            _gateway
                .Setup(g => g.CriarAsync(It.IsAny<RequisicaoGatewayPagamento>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new GatewayPagamentoException("Mercado Pago fora do ar"));

            await Assert.ThrowsAsync<GatewayPagamentoException>(() => _service.CriarAsync(NovoComando()));

            _pagamentoRepositorio.Verify(r => r.AdicionarAsync(It.IsAny<Pagamento>()), Times.Never);
        }

        [Fact]
        public async Task CriarAsync_EnviaORequisicaoIdComoChaveDeIdempotenciaDoProvedor()
        {
            ComOrcamento(valor: 450m);
            ComGatewayRespondendo(StatusPagamento.Aprovado, "accredited");

            var command = NovoComando();

            await _service.CriarAsync(command);

            _gateway.Verify(g => g.CriarAsync(
                It.Is<RequisicaoGatewayPagamento>(r => r.ChaveIdempotencia == command.RequisicaoId),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        // ─── Sincronização ──────────────────────────────────────────────────────

        [Fact]
        public async Task SincronizarAsync_PendenteParaAprovado_AtualizaEPublica()
        {
            var pagamento = PagamentoPendente();

            _pagamentoRepositorio.Setup(r => r.ObterPorIdAsync("7")).ReturnsAsync(pagamento);
            ComGatewayConsultando(StatusPagamento.Aprovado, "accredited", DateTime.UtcNow);

            var atualizado = await _service.SincronizarAsync(7);

            Assert.NotNull(atualizado);
            Assert.Equal(StatusPagamento.Aprovado, atualizado!.Status);
            _pagamentoRepositorio.Verify(r => r.AtualizarAsync(pagamento), Times.Once);
            VerificaPublicacao(PagamentoService.EventoPagamentoAprovado);
        }

        [Fact]
        public async Task SincronizarAsync_ProvedorRepeteOMesmoStatus_NaoAtualiza()
        {
            var pagamento = PagamentoPendente();

            _pagamentoRepositorio.Setup(r => r.ObterPorIdAsync("7")).ReturnsAsync(pagamento);
            ComGatewayConsultando(StatusPagamento.Pendente, "pending_contingency", DateTime.UtcNow);

            var atualizado = await _service.SincronizarAsync(7);

            Assert.Null(atualizado);
            _pagamentoRepositorio.Verify(r => r.AtualizarAsync(It.IsAny<Pagamento>()), Times.Never);
        }

        [Fact]
        public async Task SincronizarAsync_AtualizacaoAtrasada_NaoRegrideOStatus()
        {
            var pagamento = PagamentoPendente();
            pagamento.Status = StatusPagamento.Aprovado;
            pagamento.UltimaAtualizacaoProvedor = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

            _pagamentoRepositorio.Setup(r => r.ObterPorIdAsync("7")).ReturnsAsync(pagamento);

            // Notificação mais antiga que a última aplicada, dizendo que está pendente.
            ComGatewayConsultando(
                StatusPagamento.Pendente,
                "pending_contingency",
                new DateTime(2026, 11, 6, 11, 59, 0, DateTimeKind.Utc));

            var atualizado = await _service.SincronizarAsync(7);

            Assert.Null(atualizado);
            Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        }

        [Fact]
        public async Task SincronizarAsync_PagamentoInexistente_LancaKeyNotFound()
        {
            _pagamentoRepositorio.Setup(r => r.ObterPorIdAsync("99")).ReturnsAsync((Pagamento?)null);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SincronizarAsync(99));
        }

        [Fact]
        public async Task SincronizarPorIdExternoAsync_IdDesconhecido_RetornaNullSemConsultarProvedor()
        {
            _pagamentoRepositorio
                .Setup(r => r.ObterPorIdExternoAsync("nao-e-nosso"))
                .ReturnsAsync((Pagamento?)null);

            var resultado = await _service.SincronizarPorIdExternoAsync("nao-e-nosso");

            Assert.Null(resultado);
            _gateway.Verify(g => g.ObterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SincronizarPorIdExternoAsync_PagamentoConhecido_AplicaEstado()
        {
            var pagamento = PagamentoPendente();

            _pagamentoRepositorio
                .Setup(r => r.ObterPorIdExternoAsync("mp-123"))
                .ReturnsAsync(pagamento);

            ComGatewayConsultando(StatusPagamento.Aprovado, "accredited", DateTime.UtcNow);

            var resultado = await _service.SincronizarPorIdExternoAsync("mp-123");

            Assert.NotNull(resultado);
            Assert.Equal(StatusPagamento.Aprovado, resultado!.Status);
        }

        // ─── Cancelamento por valor alterado ────────────────────────────────────

        [Fact]
        public async Task CancelarEmAbertoAsync_CancelaPendenteEPublica()
        {
            var pendente = PagamentoPendente();

            _pagamentoRepositorio
                .Setup(r => r.ObterEmAbertoPorOrcamentoIdAsync(1))
                .ReturnsAsync(new[] { pendente });

            var cancelados = await _service.CancelarEmAbertoAsync(1, MotivoCancelamento.ValorDoOrcamentoAlterado);

            Assert.Equal(1, cancelados);
            Assert.Equal(StatusPagamento.Cancelado, pendente.Status);
            Assert.Equal(MotivoCancelamento.ValorDoOrcamentoAlterado, pendente.MotivoCancelamento);
            VerificaPublicacao(PagamentoService.EventoPagamentoCancelado);
        }

        [Fact]
        public async Task CancelarEmAbertoAsync_SemPagamentoEmAberto_NaoFazNada()
        {
            _pagamentoRepositorio
                .Setup(r => r.ObterEmAbertoPorOrcamentoIdAsync(1))
                .ReturnsAsync(Array.Empty<Pagamento>());

            var cancelados = await _service.CancelarEmAbertoAsync(1, MotivoCancelamento.ValorDoOrcamentoAlterado);

            Assert.Equal(0, cancelados);
            _pagamentoRepositorio.Verify(r => r.AtualizarAsync(It.IsAny<Pagamento>()), Times.Never);
        }

        [Fact]
        public async Task CancelarEmAbertoAsync_NaoCancelaPagamentoAprovado()
        {
            // Mesmo que o repositório devolva um aprovado por engano, a entidade recusa:
            // dinheiro capturado não se cancela, se estorna.
            var aprovado = new Pagamento { Id = 7, OrcamentoId = 1, Status = StatusPagamento.Aprovado };

            _pagamentoRepositorio
                .Setup(r => r.ObterEmAbertoPorOrcamentoIdAsync(1))
                .ReturnsAsync(new[] { aprovado });

            var cancelados = await _service.CancelarEmAbertoAsync(1, MotivoCancelamento.ValorDoOrcamentoAlterado);

            Assert.Equal(0, cancelados);
            Assert.Equal(StatusPagamento.Aprovado, aprovado.Status);
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private void ComOrcamento(decimal valor, int orcamentoId = 1, int ordemServicoId = 12)
        {
            _orcamentoRepositorio
                .Setup(r => r.ObterPorIdAsync(orcamentoId.ToString()))
                .ReturnsAsync(new Orcamento
                {
                    Id = orcamentoId,
                    OrdemServicoId = ordemServicoId,
                    ValorTotalGeral = valor
                });
        }

        private void ComGatewayRespondendo(StatusPagamento status, string detalhe)
        {
            _gateway
                .Setup(g => g.CriarAsync(It.IsAny<RequisicaoGatewayPagamento>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RespostaGatewayPagamento
                {
                    IdExterno = "mp-123",
                    Status = status,
                    StatusDetalhe = detalhe,
                    ModoProducao = false,
                    UltimaAtualizacao = DateTime.UtcNow
                });
        }

        private void ComGatewayConsultando(StatusPagamento status, string detalhe, DateTime ultimaAtualizacao)
        {
            _gateway
                .Setup(g => g.ObterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RespostaGatewayPagamento
                {
                    IdExterno = "mp-123",
                    Status = status,
                    StatusDetalhe = detalhe,
                    ModoProducao = false,
                    UltimaAtualizacao = ultimaAtualizacao
                });
        }

        private void VerificaPublicacao(string tipoEvento)
        {
            _publicador.Verify(p => p.PublicarAsync(
                tipoEvento,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        private static Pagamento PagamentoPendente() => new()
        {
            Id = 7,
            OrcamentoId = 1,
            OrdemServicoId = 12,
            Valor = 450m,
            Status = StatusPagamento.Pendente,
            IdExterno = "mp-123",
            Provedor = Pagamento.ProvedorMercadoPago,
            CriadoEm = DateTime.UtcNow,
            AtualizadoEm = DateTime.UtcNow
        };

        private static CriarPagamentoCommand NovoComando() => new()
        {
            RequisicaoId = Guid.NewGuid(),
            OrcamentoId = 1,
            Token = "tok_teste_123",
            MetodoPagamento = "master",
            Parcelas = 1,
            PagadorEmail = "test_user@testuser.com",
            PagadorTipoDocumento = "CPF",
            PagadorDocumento = "12345678909"
        };
    }
}
