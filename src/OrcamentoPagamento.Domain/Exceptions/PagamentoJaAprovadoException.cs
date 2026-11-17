namespace OrcamentoPagamento.Domain.Exceptions
{
    /// <summary>
    /// O orçamento já tem um pagamento aprovado. Vira 409 na API: não é erro de entrada,
    /// é conflito de estado.
    /// </summary>
    public class PagamentoJaAprovadoException : Exception
    {
        public PagamentoJaAprovadoException(int orcamentoId, int pagamentoId)
            : base($"O orcamento {orcamentoId} ja possui o pagamento aprovado {pagamentoId}.")
        {
            OrcamentoId = orcamentoId;
            PagamentoId = pagamentoId;
        }

        public int OrcamentoId { get; }
        public int PagamentoId { get; }
    }
}
