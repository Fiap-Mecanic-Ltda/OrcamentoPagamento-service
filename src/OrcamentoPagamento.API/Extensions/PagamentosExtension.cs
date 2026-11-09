using OrcamentoPagamento.Domain.Interfaces.Gateways;
using OrcamentoPagamento.Infrastructure.Gateways;

namespace OrcamentoPagamento.API.Extensions
{
    public static class PagamentosExtension
    {
        /// <summary>
        /// Registra o gateway de pagamento. Com Pagamentos:Habilitado = false, o serviço
        /// sobe com o gateway fake e nunca fala com o Mercado Pago — é o padrão em
        /// desenvolvimento e nos testes de integração, e o que mantém a CI independente
        /// de sandbox de terceiro.
        /// </summary>
        public static IServiceCollection AddPagamentos(this IServiceCollection services, IConfiguration configuration)
        {
            var secao = configuration.GetSection(PagamentoOptions.SecaoConfiguracao);
            services.Configure<PagamentoOptions>(secao);

            var options = secao.Get<PagamentoOptions>() ?? new PagamentoOptions();

            // A validação de assinatura é registrada nos dois modos: o webhook é rota
            // pública mesmo com o gateway fake.
            services.AddScoped<IValidadorAssinaturaWebhook, ValidadorAssinaturaMercadoPago>();

            if (!options.Habilitado)
            {
                services.AddScoped<IGatewayPagamento, GatewayPagamentoFake>();
                return services;
            }

            services.AddHttpClient<IGatewayPagamento, MercadoPagoGateway>(client =>
            {
                client.BaseAddress = new Uri(options.MercadoPago.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.MercadoPago.TimeoutSegundos);
            });

            return services;
        }
    }
}
