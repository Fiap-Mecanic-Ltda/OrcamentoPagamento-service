namespace OrcamentoPagamento.Domain.Exceptions
{
    /// <summary>
    /// Falha ao falar com o provedor de pagamento (rede, credencial, resposta inesperada).
    /// Vira 502 na API: o problema não é do cliente.
    /// </summary>
    public class GatewayPagamentoException : Exception
    {
        public GatewayPagamentoException(string mensagem) : base(mensagem) { }

        public GatewayPagamentoException(string mensagem, Exception innerException)
            : base(mensagem, innerException) { }
    }
}
