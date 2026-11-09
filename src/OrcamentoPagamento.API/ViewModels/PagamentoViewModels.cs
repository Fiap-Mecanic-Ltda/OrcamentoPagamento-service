using System.ComponentModel.DataAnnotations;

namespace OrcamentoPagamento.API.ViewModels
{
    public class PagamentoCreateViewModel
    {
        /// <summary>
        /// GUID gerado pelo Web. Reenvio com o mesmo valor devolve o pagamento existente
        /// em vez de cobrar de novo. Opcional, mas sem ele o retry do cliente fica sem
        /// proteção contra cobrança dupla.
        /// </summary>
        public Guid? RequisicaoId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "OrcamentoId é obrigatório.")]
        public int OrcamentoId { get; set; }

        /// <summary>Token do cartão gerado no front-end pelo SDK do Mercado Pago. Uso único.</summary>
        [Required(ErrorMessage = "O token do cartão é obrigatório.")]
        [MaxLength(200)]
        public string Token { get; set; } = string.Empty;

        /// <summary>master, visa, amex, elo… vem do próprio SDK no Web.</summary>
        [Required(ErrorMessage = "O paymentMethodId é obrigatório.")]
        [MaxLength(30)]
        public string PaymentMethodId { get; set; } = string.Empty;

        public long? IssuerId { get; set; }

        [Range(1, 24, ErrorMessage = "O número de parcelas deve estar entre 1 e 24.")]
        public int Installments { get; set; } = 1;

        [MaxLength(255)]
        public string? Descricao { get; set; }

        [Required(ErrorMessage = "Os dados do pagador são obrigatórios.")]
        public PagadorViewModel Payer { get; set; } = new();
    }

    public class PagadorViewModel
    {
        /// <summary>
        /// Precisa ser diferente do e-mail da conta vendedora do Mercado Pago, senão o
        /// pagamento falha por um motivo que não parece ser esse.
        /// </summary>
        [Required(ErrorMessage = "O e-mail do pagador é obrigatório.")]
        [EmailAddress(ErrorMessage = "E-mail do pagador inválido.")]
        [MaxLength(200)]
        public string Email { get; set; } = string.Empty;

        public IdentificacaoViewModel? Identification { get; set; }
    }

    public class IdentificacaoViewModel
    {
        [MaxLength(10)]
        public string? Type { get; set; }

        [MaxLength(30)]
        public string? Number { get; set; }
    }

    /// <summary>
    /// Corpo da notificação do Mercado Pago. Shape do provedor, por isso mora no host —
    /// mesma razão pela qual o consumidor do SQS também fica aqui.
    /// </summary>
    public class NotificacaoMercadoPagoViewModel
    {
        public long? Id { get; set; }

        /// <summary>false quando a cobrança foi feita com credenciais de teste.</summary>
        public bool LiveMode { get; set; }

        /// <summary>Tipo do recurso: payment, plan, subscription…</summary>
        public string? Type { get; set; }

        /// <summary>Nome usado pelo formato antigo de notificação (IPN).</summary>
        public string? Topic { get; set; }

        /// <summary>Evento que originou a notificação, ex.: payment.updated.</summary>
        public string? Action { get; set; }

        public DateTime? DateCreated { get; set; }

        public long? UserId { get; set; }

        public string? ApiVersion { get; set; }

        public DadosNotificacaoViewModel? Data { get; set; }
    }

    public class DadosNotificacaoViewModel
    {
        /// <summary>Id do pagamento no Mercado Pago.</summary>
        public string? Id { get; set; }
    }
}
