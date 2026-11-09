namespace OrcamentoPagamento.Domain.Interfaces.Gateways
{
    /// <summary>
    /// Valida a autenticidade de uma notificação recebida na rota pública do webhook.
    /// A implementação é específica do provedor; o host só precisa de um sim ou não.
    /// </summary>
    public interface IValidadorAssinaturaWebhook
    {
        /// <param name="assinatura">Header de assinatura (no Mercado Pago, `x-signature`).</param>
        /// <param name="requestId">Header de correlação do provedor (`x-request-id`).</param>
        /// <param name="dataId">Id do recurso notificado, como veio na requisição.</param>
        bool Validar(string? assinatura, string? requestId, string? dataId);
    }
}
