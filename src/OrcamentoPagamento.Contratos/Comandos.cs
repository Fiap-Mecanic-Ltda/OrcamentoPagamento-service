namespace MechanicLtda.Contratos
{
    // Comandos do OS Service. Todos são publicados num exchange fanout com o nome da mensagem;
    // o serviço que executa cria a própria fila e faz o bind.

    /// <summary>Para a Execução: diagnosticar o veículo.</summary>
    public record IniciarDiagnostico : MensagemSaga
    {
        public int ClienteId { get; init; }
        public int VeiculoId { get; init; }
        public string DescricaoProblema { get; init; } = string.Empty;
    }

    /// <summary>Para o Billing: gerar o orçamento e enviá-lo ao cliente para aprovação.</summary>
    public record GerarOrcamento : MensagemSaga
    {
        public int ClienteId { get; init; }
        public string NomeCliente { get; init; } = string.Empty;
        public string EmailCliente { get; init; } = string.Empty;
        public IReadOnlyList<ItemOrdemServico> Itens { get; init; } = [];
    }

    /// <summary>Para a Execução: executar o reparo (pagamento confirmado).</summary>
    public record IniciarExecucao : MensagemSaga
    {
        public int OrcamentoId { get; init; }
        public IReadOnlyList<ItemOrdemServico> Itens { get; init; } = [];
    }

    /// <summary>Compensação, para o Billing.</summary>
    public record CancelarOrcamento : MensagemSaga
    {
        public int OrcamentoId { get; init; }
        public string Motivo { get; init; } = string.Empty;
    }

    /// <summary>Compensação, para o Billing.</summary>
    public record EstornarPagamento : MensagemSaga
    {
        public int PagamentoId { get; init; }
        public string Motivo { get; init; } = string.Empty;
    }

    /// <summary>Compensação, para a Execução. Sem resposta (melhor esforço, idempotente).</summary>
    public record LiberarReservaEstoque : MensagemSaga
    {
        public string Motivo { get; init; } = string.Empty;
    }
}
