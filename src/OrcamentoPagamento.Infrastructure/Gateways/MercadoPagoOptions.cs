namespace OrcamentoPagamento.Infrastructure.Gateways
{
    /// <summary>
    /// Configuração do pagamento, seção "Pagamentos" do appsettings / variáveis de
    /// ambiente. Em produção, os segredos vêm do SSM Parameter Store via Secret do
    /// Kubernetes — nunca do appsettings.
    /// </summary>
    public class PagamentoOptions
    {
        public const string SecaoConfiguracao = "Pagamentos";

        /// <summary>
        /// Quando false, usa o gateway fake: o fluxo inteiro roda sem tocar no Mercado
        /// Pago. É o padrão em desenvolvimento e nos testes.
        /// </summary>
        public bool Habilitado { get; set; }

        public MercadoPagoOptions MercadoPago { get; set; } = new();
    }

    public class MercadoPagoOptions
    {
        public string BaseUrl { get; set; } = "https://api.mercadopago.com";

        /// <summary>
        /// Access token. Enquanto só rodamos teste, é o token com prefixo TEST-.
        /// Precisa ser da **mesma aplicação** cuja public key o Web usa para tokenizar,
        /// senão o token do cartão é rejeitado.
        /// </summary>
        public string? AccessToken { get; set; }

        /// <summary>Secret usado na validação do header x-signature do webhook.</summary>
        public string? WebhookSecret { get; set; }

        /// <summary>URL pública do webhook, registrada no painel e enviada em cada cobrança.</summary>
        public string? NotificationUrl { get; set; }

        public int TimeoutSegundos { get; set; } = 30;
    }
}
