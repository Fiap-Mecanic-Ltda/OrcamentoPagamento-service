namespace OrcamentoPagamento.Domain.Commands
{
    /// <summary>
    /// Entrada da criação de pagamento. Repare que não há valor: ele é lido do orçamento
    /// no servidor. Ver docs/pagamento.md, seção 2.1.
    /// </summary>
    public class CriarPagamentoCommand
    {
        /// <summary>Gerado pelo Web. Reenvio com o mesmo valor não cobra de novo.</summary>
        public Guid RequisicaoId { get; set; } = Guid.NewGuid();

        public int OrcamentoId { get; set; }

        public string Token { get; set; } = string.Empty;

        public string MetodoPagamento { get; set; } = string.Empty;

        public long? EmissorId { get; set; }

        public int Parcelas { get; set; } = 1;

        public string? Descricao { get; set; }

        public string PagadorEmail { get; set; } = string.Empty;

        public string? PagadorTipoDocumento { get; set; }

        public string? PagadorDocumento { get; set; }
    }
}
