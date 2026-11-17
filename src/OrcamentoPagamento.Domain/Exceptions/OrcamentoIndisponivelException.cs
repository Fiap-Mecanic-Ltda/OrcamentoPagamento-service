using OrcamentoPagamento.Domain.Enums;

namespace OrcamentoPagamento.Domain.Exceptions
{
    /// <summary>
    /// O orçamento não está em aberto (foi recusado, cancelado ou já pago) e não aceita a
    /// operação pedida. Vira 409 na API: é conflito de estado, não erro de entrada.
    /// </summary>
    public class OrcamentoIndisponivelException : Exception
    {
        public OrcamentoIndisponivelException(int orcamentoId, StatusOrcamento status, string operacao)
            : base($"O orcamento {orcamentoId} esta {status} e nao aceita {operacao}.")
        {
            OrcamentoId = orcamentoId;
            Status = status;
        }

        public int OrcamentoId { get; }
        public StatusOrcamento Status { get; }
    }
}
