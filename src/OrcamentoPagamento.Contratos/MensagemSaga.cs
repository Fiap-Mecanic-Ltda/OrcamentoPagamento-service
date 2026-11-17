namespace MechanicLtda.Contratos
{
    /// <summary>
    /// Campos presentes em todas as mensagens da Saga (docs/contratos-eventos.md).
    /// O JSON trafega sem envelope, com as propriedades em camelCase.
    /// </summary>
    public abstract record MensagemSaga
    {
        /// <summary>Id da Saga (uma por OS), gerado pelo OS Service na abertura.</summary>
        public Guid CorrelationId { get; init; }

        public int OrdemServicoId { get; init; }

        /// <summary>Quando a mensagem foi gerada (UTC).</summary>
        public DateTime DataHora { get; init; }
    }

    /// <summary>Item da OS como trafega nas mensagens. <c>Tipo</c>: Peca, Insumo ou Servico.</summary>
    public record ItemOrdemServico
    {
        public string Descricao { get; init; } = string.Empty;
        public string Tipo { get; init; } = string.Empty;
        public int Quantidade { get; init; }
        public decimal ValorUnitario { get; init; }
        public int? EstoqueId { get; init; }
        public int? ServicoOficinaId { get; init; }
    }
}
