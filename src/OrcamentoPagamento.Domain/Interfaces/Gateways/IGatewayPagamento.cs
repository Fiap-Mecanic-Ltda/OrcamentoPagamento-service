using OrcamentoPagamento.Domain.Enums;

namespace OrcamentoPagamento.Domain.Interfaces.Gateways
{
    /// <summary>
    /// Porta do provedor de pagamento. O domínio fala esta linguagem; traduzir para o
    /// vocabulário do Mercado Pago (ou de outro provedor) é trabalho do adapter.
    ///
    /// Mesmo papel que IPublicadorEventos tem para o SNS: uma implementação real e uma
    /// fake, escolhidas por configuração.
    /// </summary>
    public interface IGatewayPagamento
    {
        /// <summary>Nome do provedor, gravado no pagamento.</summary>
        string Nome { get; }

        /// <summary>
        /// Cria a cobrança no provedor. Deve lançar <see cref="Exceptions.GatewayPagamentoException"/>
        /// quando a falha é de comunicação, e devolver resultado normal quando o provedor
        /// responde recusando — recusa é resultado de negócio, não erro.
        /// </summary>
        Task<RespostaGatewayPagamento> CriarAsync(RequisicaoGatewayPagamento requisicao, CancellationToken cancellationToken = default);

        /// <summary>
        /// Consulta o estado atual da cobrança. É o que confirma o status depois de uma
        /// notificação — o corpo do webhook nunca é fonte de verdade.
        /// </summary>
        Task<RespostaGatewayPagamento> ObterAsync(string idExterno, CancellationToken cancellationToken = default);
    }

    /// <summary>Dados necessários para criar a cobrança, já na nossa linguagem.</summary>
    public class RequisicaoGatewayPagamento
    {
        /// <summary>Valor a cobrar. Vem do orçamento, nunca do cliente.</summary>
        public decimal Valor { get; set; }

        /// <summary>Token do cartão, gerado no front-end. Uso único.</summary>
        public string Token { get; set; } = string.Empty;

        public string? Descricao { get; set; }

        public int Parcelas { get; set; } = 1;

        /// <summary>master, visa, amex, elo… vem do SDK no front-end.</summary>
        public string MetodoPagamento { get; set; } = string.Empty;

        public long? EmissorId { get; set; }

        public string PagadorEmail { get; set; } = string.Empty;

        public string? PagadorTipoDocumento { get; set; }

        public string? PagadorDocumento { get; set; }

        /// <summary>
        /// Referência nossa levada ao provedor, para amarrar a notificação de volta.
        /// Usamos o id do orçamento.
        /// </summary>
        public string? ReferenciaExterna { get; set; }

        /// <summary>
        /// Chave de idempotência enviada ao provedor. É o RequisicaoId — garante que um
        /// retry nosso não gere segunda cobrança do outro lado.
        /// </summary>
        public Guid ChaveIdempotencia { get; set; }
    }

    /// <summary>Estado da cobrança no provedor, traduzido.</summary>
    public class RespostaGatewayPagamento
    {
        public string? IdExterno { get; set; }

        public StatusPagamento Status { get; set; }

        /// <summary>Detalhe cru do provedor, preservado para diagnóstico.</summary>
        public string? StatusDetalhe { get; set; }

        public string? MetodoPagamento { get; set; }

        public int? Parcelas { get; set; }

        /// <summary>false quando a cobrança foi feita com credenciais de teste.</summary>
        public bool ModoProducao { get; set; }

        /// <summary>Relógio do provedor, usado para descartar notificação atrasada.</summary>
        public DateTime? UltimaAtualizacao { get; set; }
    }
}
