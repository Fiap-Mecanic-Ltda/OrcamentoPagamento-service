using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Enums;
using Xunit;

namespace OrcamentoPagamento.Domain.Tests
{
    /// <summary>
    /// A máquina de estados é a única coisa que impede uma notificação atrasada do provedor
    /// de fazer um pagamento aprovado voltar a pendente. Testada direto na entidade.
    /// </summary>
    public class PagamentoMaquinaDeEstadosTests
    {
        [Theory]
        [InlineData(StatusPagamento.Pendente, StatusPagamento.Aprovado, true)]
        [InlineData(StatusPagamento.Pendente, StatusPagamento.Recusado, true)]
        [InlineData(StatusPagamento.Pendente, StatusPagamento.Cancelado, true)]
        [InlineData(StatusPagamento.EmProcessamento, StatusPagamento.Aprovado, true)]
        [InlineData(StatusPagamento.Autorizado, StatusPagamento.Aprovado, true)]
        // Aprovado só sai para os estados de devolução.
        [InlineData(StatusPagamento.Aprovado, StatusPagamento.Devolvido, true)]
        [InlineData(StatusPagamento.Aprovado, StatusPagamento.EmDisputa, true)]
        [InlineData(StatusPagamento.Aprovado, StatusPagamento.Estornado, true)]
        [InlineData(StatusPagamento.Aprovado, StatusPagamento.Pendente, false)]
        [InlineData(StatusPagamento.Aprovado, StatusPagamento.Recusado, false)]
        [InlineData(StatusPagamento.Aprovado, StatusPagamento.Cancelado, false)]
        // Recusado e Cancelado são terminais.
        [InlineData(StatusPagamento.Recusado, StatusPagamento.Aprovado, false)]
        [InlineData(StatusPagamento.Recusado, StatusPagamento.Pendente, false)]
        [InlineData(StatusPagamento.Cancelado, StatusPagamento.Aprovado, false)]
        [InlineData(StatusPagamento.Estornado, StatusPagamento.Aprovado, false)]
        // Mesmo status não é transição.
        [InlineData(StatusPagamento.Pendente, StatusPagamento.Pendente, false)]
        public void PodeTransicionarPara_RespeitaAsRegras(StatusPagamento atual, StatusPagamento novo, bool esperado)
        {
            var pagamento = new Pagamento { Status = atual };

            Assert.Equal(esperado, pagamento.PodeTransicionarPara(novo));
        }

        [Fact]
        public void AplicarStatusDoProvedor_TransicaoValida_AtualizaOsCampos()
        {
            var pagamento = new Pagamento { Status = StatusPagamento.Pendente };
            var agora = DateTime.UtcNow;

            var mudou = pagamento.AplicarStatusDoProvedor(
                StatusPagamento.Aprovado, "accredited", "mp-999", "visa", 3, modoTeste: true, agora);

            Assert.True(mudou);
            Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
            Assert.Equal("accredited", pagamento.StatusDetalhe);
            Assert.Equal("mp-999", pagamento.IdExterno);
            Assert.Equal("visa", pagamento.MetodoPagamento);
            Assert.Equal(3, pagamento.Parcelas);
            Assert.True(pagamento.ModoTeste);
            Assert.Equal(agora, pagamento.UltimaAtualizacaoProvedor);
        }

        [Fact]
        public void AplicarStatusDoProvedor_TransicaoInvalida_NaoMudaNada()
        {
            var pagamento = new Pagamento
            {
                Status = StatusPagamento.Aprovado,
                StatusDetalhe = "accredited"
            };

            var mudou = pagamento.AplicarStatusDoProvedor(
                StatusPagamento.Pendente, "pending_contingency", "mp-999", "visa", 1, false, DateTime.UtcNow);

            Assert.False(mudou);
            Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
            Assert.Equal("accredited", pagamento.StatusDetalhe);
        }

        [Fact]
        public void AplicarStatusDoProvedor_AtualizacaoAtrasada_NaoMudaNada()
        {
            var pagamento = new Pagamento
            {
                Status = StatusPagamento.Pendente,
                UltimaAtualizacaoProvedor = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc)
            };

            var mudou = pagamento.AplicarStatusDoProvedor(
                StatusPagamento.Aprovado,
                "accredited",
                "mp-999",
                "visa",
                1,
                false,
                new DateTime(2026, 11, 6, 11, 0, 0, DateTimeKind.Utc));

            Assert.False(mudou);
            Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
        }

        [Fact]
        public void DeveDescartarAtualizacao_SemRelogioDeNenhumDosLados_NaoDescarta()
        {
            var pagamento = new Pagamento { Status = StatusPagamento.Pendente };

            Assert.False(pagamento.DeveDescartarAtualizacao(null));
            Assert.False(pagamento.DeveDescartarAtualizacao(DateTime.UtcNow));
        }

        [Theory]
        [InlineData(StatusPagamento.Pendente, true)]
        [InlineData(StatusPagamento.EmProcessamento, true)]
        [InlineData(StatusPagamento.Autorizado, true)]
        [InlineData(StatusPagamento.Aprovado, false)]
        [InlineData(StatusPagamento.Recusado, false)]
        [InlineData(StatusPagamento.Cancelado, false)]
        public void EstaEmAberto_SoValeParaOsStatusQueAindaPodemMudarPorPagamento(StatusPagamento status, bool esperado)
        {
            Assert.Equal(esperado, new Pagamento { Status = status }.EstaEmAberto());
        }

        [Fact]
        public void Cancelar_PagamentoEmAberto_CancelaEGravaOMotivo()
        {
            var pagamento = new Pagamento { Status = StatusPagamento.Pendente };

            Assert.True(pagamento.Cancelar(MotivoCancelamento.ValorDoOrcamentoAlterado));
            Assert.Equal(StatusPagamento.Cancelado, pagamento.Status);
            Assert.Equal(MotivoCancelamento.ValorDoOrcamentoAlterado, pagamento.MotivoCancelamento);
        }

        [Fact]
        public void Cancelar_PagamentoAprovado_NaoCancela()
        {
            var pagamento = new Pagamento { Status = StatusPagamento.Aprovado };

            Assert.False(pagamento.Cancelar(MotivoCancelamento.ValorDoOrcamentoAlterado));
            Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
            Assert.Null(pagamento.MotivoCancelamento);
        }
    }
}
