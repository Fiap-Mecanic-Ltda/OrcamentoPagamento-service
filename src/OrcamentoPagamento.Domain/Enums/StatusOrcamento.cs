namespace OrcamentoPagamento.Domain.Enums
{
    /// <summary>
    /// Situação do orçamento na Saga da OS (docs/contratos-eventos.md, "Regra do pagamento").
    /// Só orçamento <see cref="Aberto"/> ou <see cref="Aprovado"/> aceita pagamento: equivale à
    /// OS em Aguardando Aprovação.
    /// </summary>
    public enum StatusOrcamento
    {
        /// <summary>Gerado e enviado ao cliente (OrcamentoGerado publicado).</summary>
        Aberto = 1,

        /// <summary>Aprovado pelo cliente sem pagamento. Só informativo: ainda falta pagar.</summary>
        Aprovado = 2,

        /// <summary>Pago: o PagamentoConfirmado já foi publicado.</summary>
        Pago = 3,

        /// <summary>Recusado pelo cliente (OrcamentoRecusado publicado). Terminal.</summary>
        Recusado = 4,

        /// <summary>Cancelado pela compensação da Saga (CancelarOrcamento). Terminal.</summary>
        Cancelado = 5
    }
}
