using Microsoft.Extensions.Logging;
using OrcamentoPagamento.Domain.Enums;
using OrcamentoPagamento.Domain.Interfaces.Gateways;
using System.Collections.Concurrent;

namespace OrcamentoPagamento.Infrastructure.Gateways
{
    /// <summary>
    /// Gateway usado quando `Pagamentos:Habilitado = false`: desenvolvimento local sem AWS
    /// nem Mercado Pago, e os testes automatizados. Mesmo papel do PublicadorEventosLog
    /// para o SNS.
    ///
    /// O resultado é decidido pelo **token**, usando os mesmos códigos de quatro letras que
    /// o Mercado Pago usa no nome do titular do cartão de teste. Num ambiente real esses
    /// códigos ficam dentro do token e o gateway não os vê; aqui o token é texto livre,
    /// então vale como convenção:
    ///
    ///   token "APRO..." → Aprovado          token "FUND..." → Recusado (saldo)
    ///   token "CONT..." → Pendente          token "SECU..." → Recusado (CVV)
    ///   token "CALL..." → Recusado          token "OTHE..." → Recusado (erro geral)
    ///   qualquer outro  → Aprovado
    ///
    /// Um pagamento criado como `CONT` (Pendente) vira Aprovado na primeira consulta
    /// seguinte — é o que permite exercitar webhook e máquina de estados sem o provedor.
    /// </summary>
    public class GatewayPagamentoFake : IGatewayPagamento
    {
        private static readonly ConcurrentDictionary<string, EstadoFake> Estados = new();

        private readonly ILogger<GatewayPagamentoFake> _logger;

        public GatewayPagamentoFake(ILogger<GatewayPagamentoFake> logger)
        {
            _logger = logger;
        }

        public string Nome => Domain.Entities.Pagamento.ProvedorMercadoPago;

        public Task<RespostaGatewayPagamento> CriarAsync(RequisicaoGatewayPagamento requisicao, CancellationToken cancellationToken = default)
        {
            var (status, detalhe) = Decidir(requisicao.Token);
            var idExterno = $"fake-{Guid.NewGuid():N}"[..20];

            Estados[idExterno] = new EstadoFake(status, detalhe, DateTime.UtcNow);

            _logger.LogWarning(
                "Pagamentos desligados: cobranca de {Valor} simulada pelo gateway fake. Token '{Token}' -> {Status}. IdExterno {IdExterno}.",
                requisicao.Valor, requisicao.Token, status, idExterno);

            return Task.FromResult(new RespostaGatewayPagamento
            {
                IdExterno = idExterno,
                Status = status,
                StatusDetalhe = detalhe,
                MetodoPagamento = requisicao.MetodoPagamento,
                Parcelas = requisicao.Parcelas,
                ModoProducao = false,
                UltimaAtualizacao = DateTime.UtcNow
            });
        }

        public Task<RespostaGatewayPagamento> ObterAsync(string idExterno, CancellationToken cancellationToken = default)
        {
            if (!Estados.TryGetValue(idExterno, out var estado))
            {
                // Pagamento que o fake não criou (banco de outra execução). Devolve o que
                // temos de mais inócuo: pendente, sem avançar nada.
                _logger.LogWarning(
                    "Gateway fake nao conhece o pagamento {IdExterno}. Respondendo Pendente.", idExterno);

                return Task.FromResult(new RespostaGatewayPagamento
                {
                    IdExterno = idExterno,
                    Status = StatusPagamento.Pendente,
                    StatusDetalhe = "fake_desconhecido",
                    ModoProducao = false,
                    UltimaAtualizacao = DateTime.UtcNow
                });
            }

            // Pendente avança para Aprovado na consulta: é o que torna o webhook e a
            // transição de estado testáveis sem o provedor.
            if (estado.Status == StatusPagamento.Pendente)
            {
                estado = new EstadoFake(StatusPagamento.Aprovado, "accredited", DateTime.UtcNow);
                Estados[idExterno] = estado;
            }

            return Task.FromResult(new RespostaGatewayPagamento
            {
                IdExterno = idExterno,
                Status = estado.Status,
                StatusDetalhe = estado.Detalhe,
                ModoProducao = false,
                UltimaAtualizacao = estado.AtualizadoEm
            });
        }

        private static (StatusPagamento Status, string Detalhe) Decidir(string? token)
        {
            var codigo = (token ?? string.Empty).Trim().ToUpperInvariant();

            if (codigo.StartsWith("CONT", StringComparison.Ordinal))
                return (StatusPagamento.Pendente, "pending_contingency");

            if (codigo.StartsWith("FUND", StringComparison.Ordinal))
                return (StatusPagamento.Recusado, "cc_rejected_insufficient_amount");

            if (codigo.StartsWith("SECU", StringComparison.Ordinal))
                return (StatusPagamento.Recusado, "cc_rejected_bad_filled_security_code");

            if (codigo.StartsWith("CALL", StringComparison.Ordinal))
                return (StatusPagamento.Recusado, "cc_rejected_call_for_authorize");

            if (codigo.StartsWith("EXPI", StringComparison.Ordinal))
                return (StatusPagamento.Recusado, "cc_rejected_bad_filled_date");

            if (codigo.StartsWith("OTHE", StringComparison.Ordinal))
                return (StatusPagamento.Recusado, "cc_rejected_other_reason");

            return (StatusPagamento.Aprovado, "accredited");
        }

        /// <summary>Limpa o estado acumulado. Usado pelos testes para isolar cenários.</summary>
        public static void Limpar() => Estados.Clear();

        private record EstadoFake(StatusPagamento Status, string Detalhe, DateTime AtualizadoEm);
    }
}
