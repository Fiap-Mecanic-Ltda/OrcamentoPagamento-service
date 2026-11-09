using Amazon.SimpleNotificationService;
using Amazon.SQS;
using OrcamentoPagamento.API.Messaging;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using OrcamentoPagamento.Infrastructure.Messaging;

namespace OrcamentoPagamento.API.Extensions
{
    public static class MessagingExtension
    {
        /// <summary>
        /// Registra o publicador SNS e o consumidor SQS. Com Messaging:Habilitado = false
        /// o serviço sobe sem tocar na AWS — é assim que o docker-compose sem LocalStack,
        /// a execução local e os testes de integração rodam.
        /// </summary>
        public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
        {
            var secao = configuration.GetSection(MessagingOptions.SecaoConfiguracao);
            services.Configure<MessagingOptions>(secao);

            var options = secao.Get<MessagingOptions>() ?? new MessagingOptions();

            if (!options.Habilitado)
            {
                services.AddSingleton<IPublicadorEventos, PublicadorEventosLog>();
                return services;
            }

            services.AddSingleton<IAmazonSimpleNotificationService>(_ =>
                new AmazonSimpleNotificationServiceClient(ConfigurarSns(options)));

            services.AddSingleton<IAmazonSQS>(_ =>
                new AmazonSQSClient(ConfigurarSqs(options)));

            services.AddScoped<IPublicadorEventos, SnsPublicadorEventos>();
            services.AddHostedService<SqsConsumidorItensAlterados>();

            return services;
        }

        private static AmazonSimpleNotificationServiceConfig ConfigurarSns(MessagingOptions options)
        {
            var config = new AmazonSimpleNotificationServiceConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Regiao)
            };

            // ServiceUrl só é usado no desenvolvimento, para apontar ao LocalStack.
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
                config.ServiceURL = options.ServiceUrl;

            return config;
        }

        private static AmazonSQSConfig ConfigurarSqs(MessagingOptions options)
        {
            var config = new AmazonSQSConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Regiao)
            };

            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
                config.ServiceURL = options.ServiceUrl;

            return config;
        }
    }
}
