namespace OrcamentoPagamento.Domain.Messaging
{
    /// <summary>
    /// X-Correlation-Id do fluxo em andamento (requisição HTTP ou mensagem recebida), para que
    /// as mensagens publicadas levem o mesmo valor no cabeçalho <c>x-correlation-id</c> e o
    /// rastreio no New Relic atravesse os serviços.
    /// </summary>
    public static class ContextoCorrelacao
    {
        /// <summary>Cabeçalho com o id da Saga (o mesmo correlationId do corpo).</summary>
        public const string CabecalhoCorrelationId = "correlation-id";

        /// <summary>Cabeçalho com o X-Correlation-Id HTTP que originou o fluxo.</summary>
        public const string CabecalhoRequisicao = "x-correlation-id";

        private static readonly AsyncLocal<string?> _atual = new();

        public static string? CorrelationIdRequisicao
        {
            get => _atual.Value;
            set => _atual.Value = value;
        }
    }
}
