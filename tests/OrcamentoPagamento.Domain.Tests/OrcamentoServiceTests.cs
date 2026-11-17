using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Enums;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using OrcamentoPagamento.Domain.Interfaces.Repositories;
using OrcamentoPagamento.Domain.Interfaces.Services;
using OrcamentoPagamento.Domain.Services;

namespace OrcamentoPagamento.Domain.Tests
{
    public class OrcamentoServiceTests
    {
        private readonly Mock<IOrcamentoRepository> _repositorio = new();
        private readonly Mock<IPublicadorEventos> _publicador = new();
        private readonly Mock<IPagamentoService> _pagamentoService = new();
        private readonly OrcamentoService _service;

        public OrcamentoServiceTests()
        {
            _repositorio
                .Setup(r => r.AdicionarAsync(It.IsAny<Orcamento>()))
                .ReturnsAsync((Orcamento o) => o);

            _repositorio
                .Setup(r => r.AtualizarAsync(It.IsAny<Orcamento>()))
                .ReturnsAsync((Orcamento o) => o);

            _service = new OrcamentoService(
                NullLogger<OrcamentoService>.Instance,
                new NotificadorService(),
                _repositorio.Object,
                _publicador.Object,
                _pagamentoService.Object);
        }

        [Fact]
        public async Task CalcularAsync_SemOrcamentoExistente_CriaSeparandoTotaisPorCategoria()
        {
            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync((Orcamento?)null);

            var command = NovoComando(10,
                Item("Pastilha de freio", CategoriaItem.Peca, 2, 100m),
                Item("Oleo 5W30", CategoriaItem.Insumo, 4, 50m),
                Item("Mao de obra - revisao", CategoriaItem.Servico, 1, 300m));

            var orcamento = await _service.CalcularAsync(command);

            Assert.NotNull(orcamento);
            Assert.Equal(200m, orcamento!.ValorTotalPecas);
            Assert.Equal(200m, orcamento.ValorTotalInsumos);
            Assert.Equal(300m, orcamento.ValorTotalServicos);
            Assert.Equal(700m, orcamento.ValorTotalGeral);
            Assert.Equal(3, orcamento.Itens.Count);
            _repositorio.Verify(r => r.AdicionarAsync(It.IsAny<Orcamento>()), Times.Once);
        }

        [Fact]
        public async Task CalcularAsync_ComOrcamentoExistente_SubstituiItensEAtualiza()
        {
            var existente = new Orcamento
            {
                Id = 1,
                OrdemServicoId = 10,
                ValorTotalGeral = 999m,
                Itens = new List<ItemOrcamento>
                {
                    new() { Descricao = "Item antigo", Categoria = CategoriaItem.Peca, Quantidade = 1, ValorUnitario = 999m }
                }
            };

            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync(existente);

            var command = NovoComando(10, Item("Correia dentada", CategoriaItem.Peca, 1, 150m));

            var orcamento = await _service.CalcularAsync(command);

            Assert.NotNull(orcamento);
            Assert.Single(orcamento!.Itens);
            Assert.Equal("Correia dentada", orcamento.Itens.First().Descricao);
            Assert.Equal(150m, orcamento.ValorTotalGeral);
            _repositorio.Verify(r => r.AtualizarAsync(existente), Times.Once);
            _repositorio.Verify(r => r.AdicionarAsync(It.IsAny<Orcamento>()), Times.Never);
        }

        [Fact]
        public async Task CalcularAsync_MesmoEventoDuasVezes_DescartaASegunda()
        {
            var eventoId = Guid.NewGuid();

            var existente = new Orcamento
            {
                Id = 1,
                OrdemServicoId = 10,
                UltimoEventoId = eventoId,
                UltimoEventoEm = DateTime.UtcNow,
                ValorTotalGeral = 150m
            };

            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync(existente);

            var command = NovoComando(10, Item("Correia dentada", CategoriaItem.Peca, 1, 999m));
            command.EventoId = eventoId;

            var orcamento = await _service.CalcularAsync(command);

            Assert.Null(orcamento);
            Assert.Equal(150m, existente.ValorTotalGeral);
            _repositorio.Verify(r => r.AtualizarAsync(It.IsAny<Orcamento>()), Times.Never);
        }

        [Fact]
        public async Task CalcularAsync_EventoMaisAntigoQueOUltimoAplicado_Descarta()
        {
            var existente = new Orcamento
            {
                Id = 1,
                OrdemServicoId = 10,
                UltimoEventoId = Guid.NewGuid(),
                UltimoEventoEm = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc),
                ValorTotalGeral = 150m
            };

            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync(existente);

            var command = NovoComando(10, Item("Item atrasado", CategoriaItem.Peca, 1, 999m));
            command.OcorridoEm = new DateTime(2026, 11, 6, 11, 59, 0, DateTimeKind.Utc);

            var orcamento = await _service.CalcularAsync(command);

            Assert.Null(orcamento);
            Assert.Equal(150m, existente.ValorTotalGeral);
        }

        [Fact]
        public async Task CalcularAsync_ComDadosDaOrdemServico_GravaSnapshotParaOPdf()
        {
            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync((Orcamento?)null);

            var command = NovoComando(10, Item("Filtro de ar", CategoriaItem.Peca, 1, 80m));
            command.OrdemServico = new OrdemServicoDadosCommand
            {
                DescricaoProblema = "Revisao dos 20 mil km",
                Status = "EmDiagnostico",
                ClienteNome = "Carlos Oliveira",
                ClienteEmail = "carlos@email.com",
                VeiculoPlaca = "ABC1D23",
                VeiculoAno = 2021
            };

            var orcamento = await _service.CalcularAsync(command);

            Assert.NotNull(orcamento?.OrdemServicoSnapshot);
            Assert.Equal("Carlos Oliveira", orcamento!.OrdemServicoSnapshot!.ClienteNome);
            Assert.Equal("ABC1D23", orcamento.OrdemServicoSnapshot.VeiculoPlaca);
            Assert.Equal(2021, orcamento.OrdemServicoSnapshot.VeiculoAno);
        }

        [Fact]
        public async Task CalcularAsync_SemItens_ZeraOsTotais()
        {
            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync((Orcamento?)null);

            var orcamento = await _service.CalcularAsync(NovoComando(10));

            Assert.NotNull(orcamento);
            Assert.Equal(0m, orcamento!.ValorTotalGeral);
            Assert.Empty(orcamento.Itens);
        }

        [Fact]
        public async Task CalcularAsync_SemOrdemServicoId_LancaArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _service.CalcularAsync(NovoComando(0)));
        }

        [Fact]
        public async Task CalcularAsync_PublicaEventoOrcamentoCalculado()
        {
            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync((Orcamento?)null);

            await _service.CalcularAsync(NovoComando(10, Item("Filtro", CategoriaItem.Peca, 1, 80m)));

            _publicador.Verify(p => p.PublicarAsync(
                OrcamentoService.EventoOrcamentoCalculado,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CalcularAsync_FalhaAoPublicar_NaoDesfazOCalculo()
        {
            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync((Orcamento?)null);

            _publicador
                .Setup(p => p.PublicarAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SNS indisponivel"));

            var orcamento = await _service.CalcularAsync(NovoComando(10, Item("Filtro", CategoriaItem.Peca, 1, 80m)));

            Assert.NotNull(orcamento);
            Assert.Equal(80m, orcamento!.ValorTotalGeral);
        }

        [Fact]
        public async Task AtualizarManualAsync_SomaAsCategoriasEMarcaOAjuste()
        {
            var existente = new Orcamento { Id = 1, OrdemServicoId = 10 };

            _repositorio
                .Setup(r => r.ObterPorIdAsync("1"))
                .ReturnsAsync(existente);

            var orcamento = await _service.AtualizarManualAsync(1, 100m, 50m, 30m, null);

            Assert.Equal(180m, orcamento.ValorTotalGeral);
            Assert.True(orcamento.AjustadoManualmente);
            Assert.NotNull(orcamento.AjustadoEm);
            _publicador.Verify(p => p.PublicarAsync(
                OrcamentoService.EventoOrcamentoAjustado,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AtualizarManualAsync_ComValorNegativo_LancaArgumentException()
        {
            _repositorio
                .Setup(r => r.ObterPorIdAsync("1"))
                .ReturnsAsync(new Orcamento { Id = 1, OrdemServicoId = 10 });

            await Assert.ThrowsAsync<ArgumentException>(
                () => _service.AtualizarManualAsync(1, -1m, 0m, 0m, null));
        }

        [Fact]
        public async Task AtualizarManualAsync_OrcamentoInexistente_LancaKeyNotFound()
        {
            _repositorio
                .Setup(r => r.ObterPorIdAsync("99"))
                .ReturnsAsync((Orcamento?)null);

            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => _service.AtualizarManualAsync(99, 10m, 10m, 10m, null));
        }

        [Fact]
        public async Task CalcularAsync_AposAjusteManual_RecalculaEDescartaOAjuste()
        {
            var existente = new Orcamento
            {
                Id = 1,
                OrdemServicoId = 10,
                AjustadoManualmente = true,
                AjustadoEm = DateTime.UtcNow,
                ValorTotalGeral = 1000m
            };

            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync(existente);

            var orcamento = await _service.CalcularAsync(
                NovoComando(10, Item("Filtro", CategoriaItem.Peca, 1, 80m)));

            Assert.NotNull(orcamento);
            Assert.False(orcamento!.AjustadoManualmente);
            Assert.Null(orcamento.AjustadoEm);
            Assert.Equal(80m, orcamento.ValorTotalGeral);
        }

        [Fact]
        public async Task RemoverAsync_OrcamentoInexistente_LancaKeyNotFound()
        {
            _repositorio
                .Setup(r => r.ObterPorIdAsync("99"))
                .ReturnsAsync((Orcamento?)null);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.RemoverAsync("99"));
        }

        [Fact]
        public async Task RemoverAsync_OrcamentoExistente_RemoveEPublica()
        {
            _repositorio
                .Setup(r => r.ObterPorIdAsync("1"))
                .ReturnsAsync(new Orcamento { Id = 1, OrdemServicoId = 10 });

            await _service.RemoverAsync("1");

            _repositorio.Verify(r => r.RemoverAsync("1"), Times.Once);
            _publicador.Verify(p => p.PublicarAsync(
                OrcamentoService.EventoOrcamentoRemovido,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CalcularAsync_QuandoOValorMuda_CancelaPagamentoEmAberto()
        {
            var existente = new Orcamento
            {
                Id = 1,
                OrdemServicoId = 10,
                Itens = new List<ItemOrcamento>
                {
                    new() { Descricao = "Filtro", Categoria = CategoriaItem.Peca, Quantidade = 1, ValorUnitario = 80m, ValorTotal = 80m }
                },
                ValorTotalGeral = 80m
            };

            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync(existente);

            await _service.CalcularAsync(NovoComando(10, Item("Correia", CategoriaItem.Peca, 1, 150m)));

            _pagamentoService.Verify(p => p.CancelarEmAbertoAsync(
                1,
                MotivoCancelamento.ValorDoOrcamentoAlterado,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CalcularAsync_QuandoOValorNaoMuda_NaoCancelaPagamento()
        {
            var existente = new Orcamento
            {
                Id = 1,
                OrdemServicoId = 10,
                Itens = new List<ItemOrcamento>
                {
                    new() { Descricao = "Filtro", Categoria = CategoriaItem.Peca, Quantidade = 1, ValorUnitario = 80m, ValorTotal = 80m }
                },
                ValorTotalGeral = 80m
            };

            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync(existente);

            // Item diferente, mesmo total: nada muda para quem esta pagando.
            await _service.CalcularAsync(NovoComando(10, Item("Outro filtro", CategoriaItem.Peca, 1, 80m)));

            _pagamentoService.Verify(p => p.CancelarEmAbertoAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CalcularAsync_OrcamentoNovo_NaoTentaCancelarPagamento()
        {
            _repositorio
                .Setup(r => r.ObterPorOrdemServicoIdAsync(10))
                .ReturnsAsync((Orcamento?)null);

            await _service.CalcularAsync(NovoComando(10, Item("Filtro", CategoriaItem.Peca, 1, 80m)));

            _pagamentoService.Verify(p => p.CancelarEmAbertoAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AtualizarManualAsync_QuandoOValorMuda_CancelaPagamentoEmAberto()
        {
            var existente = new Orcamento { Id = 1, OrdemServicoId = 10, ValorTotalGeral = 100m };

            _repositorio
                .Setup(r => r.ObterPorIdAsync("1"))
                .ReturnsAsync(existente);

            await _service.AtualizarManualAsync(1, 300m, 120m, 80m, null);

            _pagamentoService.Verify(p => p.CancelarEmAbertoAsync(
                1,
                MotivoCancelamento.ValorDoOrcamentoAlterado,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        private static CalcularOrcamentoCommand NovoComando(int ordemServicoId, params ItemCalculoCommand[] itens)
            => new()
            {
                OrdemServicoId = ordemServicoId,
                Itens = itens,
                OcorridoEm = DateTime.UtcNow
            };

        private static ItemCalculoCommand Item(string descricao, CategoriaItem categoria, int quantidade, decimal valorUnitario)
            => new()
            {
                Descricao = descricao,
                Categoria = categoria,
                Quantidade = quantidade,
                ValorUnitario = valorUnitario
            };
    }
}
