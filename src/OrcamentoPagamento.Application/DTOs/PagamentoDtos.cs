using OrcamentoPagamento.Domain.Enums;

namespace OrcamentoPagamento.Application.DTOs
{
    public class PagamentoDto
    {
        public int Id { get; set; }
        public Guid RequisicaoId { get; set; }
        public int OrcamentoId { get; set; }
        public int OrdemServicoId { get; set; }
        public decimal Valor { get; set; }
        public StatusPagamento Status { get; set; }

        /// <summary>Nome do status, para a tela não precisar conhecer o enum.</summary>
        public string StatusDescricao { get; set; } = string.Empty;

        public string? StatusDetalhe { get; set; }
        public string Provedor { get; set; } = string.Empty;
        public string? IdExterno { get; set; }
        public int Parcelas { get; set; }
        public string? MetodoPagamento { get; set; }
        public bool ModoTeste { get; set; }
        public string? MotivoCancelamento { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AtualizadoEm { get; set; }
    }

    /// <summary>
    /// Entrada da criação de pagamento. Sem campo de valor, de propósito: ele vem do
    /// orçamento no servidor. Ver docs/pagamento.md, seção 2.1.
    /// </summary>
    public class PagamentoCreateDto
    {
        public Guid? RequisicaoId { get; set; }
        public int OrcamentoId { get; set; }
        public string Token { get; set; } = string.Empty;
        public string PaymentMethodId { get; set; } = string.Empty;
        public long? IssuerId { get; set; }
        public int Installments { get; set; } = 1;
        public string? Descricao { get; set; }
        public PagadorDto Payer { get; set; } = new();
    }

    public class PagadorDto
    {
        public string Email { get; set; } = string.Empty;
        public IdentificacaoDto? Identification { get; set; }
    }

    public class IdentificacaoDto
    {
        public string? Type { get; set; }
        public string? Number { get; set; }
    }
}
