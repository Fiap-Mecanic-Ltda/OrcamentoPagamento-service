namespace MechanicLtda.Contratos
{
    // ── Eventos da Execução ────────────────────────────────────────────────────

    public record DiagnosticoConcluido : MensagemSaga
    {
        public IReadOnlyList<ItemOrdemServico> Itens { get; init; } = [];
        public string? Observacao { get; init; }
    }

    public record DiagnosticoFalhou : MensagemSaga
    {
        public string Motivo { get; init; } = string.Empty;
    }

    public record ExecucaoIniciada : MensagemSaga
    {
        public DateTime DataInicio { get; init; }
    }

    public record ExecucaoFinalizada : MensagemSaga
    {
        public DateTime DataInicio { get; init; }
        public DateTime DataFim { get; init; }
    }

    public record ExecucaoFalhou : MensagemSaga
    {
        public string Motivo { get; init; } = string.Empty;
    }

    // ── Eventos do Billing ─────────────────────────────────────────────────────

    public record OrcamentoGerado : MensagemSaga
    {
        public int OrcamentoId { get; init; }
        public decimal ValorTotal { get; init; }
        public DateTime? Validade { get; init; }
    }

    public record OrcamentoFalhou : MensagemSaga
    {
        public string Motivo { get; init; } = string.Empty;
    }

    /// <summary>
    /// Opcional e só informativo: a aprovação que conta é o pagamento confirmado, feito com a
    /// OS em Aguardando Aprovação. A OS não muda de status com este evento.
    /// </summary>
    public record OrcamentoAprovado : MensagemSaga
    {
        public int OrcamentoId { get; init; }
    }

    public record OrcamentoRecusado : MensagemSaga
    {
        public int OrcamentoId { get; init; }
        public string? Motivo { get; init; }
    }

    /// <summary>
    /// Pagamento do orçamento aprovado no Billing. Só é aceito com a OS em Aguardando Aprovação,
    /// e vale como a aprovação do cliente: a OS vai para Execução.
    /// </summary>
    public record PagamentoConfirmado : MensagemSaga
    {
        public int PagamentoId { get; init; }
        public decimal Valor { get; init; }
        public string? MeioPagamento { get; init; }
    }

    /// <summary>Tentativa de pagamento recusada. Não cancela a OS: o cliente pode pagar de novo até o prazo de aprovação.</summary>
    public record PagamentoFalhou : MensagemSaga
    {
        public string Motivo { get; init; } = string.Empty;
    }

    public record OrcamentoCancelado : MensagemSaga
    {
        public int OrcamentoId { get; init; }
    }

    public record PagamentoEstornado : MensagemSaga
    {
        public int PagamentoId { get; init; }
    }

    // ── Evento publicado pelo OS Service ───────────────────────────────────────

    /// <summary>Toda mudança de status da OS. <c>StatusAnterior</c> e <c>StatusAtual</c> usam os nomes do enum.</summary>
    public record OrdemServicoStatusAlterado : MensagemSaga
    {
        public int ClienteId { get; init; }
        public string? StatusAnterior { get; init; }
        public string StatusAtual { get; init; } = string.Empty;
        public string? Motivo { get; init; }
    }
}
