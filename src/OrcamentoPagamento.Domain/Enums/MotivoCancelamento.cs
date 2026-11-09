namespace OrcamentoPagamento.Domain.Enums
{
    /// <summary>
    /// Motivos de cancelamento originados aqui, e não no provedor. Cancelamento vindo do
    /// provedor não preenche este campo — o porquê fica no StatusDetalhe.
    /// </summary>
    public static class MotivoCancelamento
    {
        /// <summary>
        /// O recálculo do orçamento mudou o valor e o pagamento pendente deixou de valer.
        /// </summary>
        public const string ValorDoOrcamentoAlterado = "ValorDoOrcamentoAlterado";

        /// <summary>Cancelamento pedido por um operador.</summary>
        public const string CanceladoPeloOperador = "CanceladoPeloOperador";
    }
}
