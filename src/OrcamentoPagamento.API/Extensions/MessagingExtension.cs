using MassTransit;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using OrcamentoPagamento.Infrastructure.Messaging;

namespace OrcamentoPagamento.API.Extensions
{
    public static class MessagingExtension
    {
        public const string Secao = "Mensageria";

        /// <summary>
        /// Participação na Saga da OS pelo RabbitMQ (MassTransit 8), como o OS Service e a
        /// Execução. Ver docs/contratos-eventos.md e docs/adr/ADR-004-saga-rabbitmq.md.
        ///
        /// Mensageria:Habilitada=false (padrão): a API funciona sozinha, não conecta no
        /// RabbitMQ e as mensagens da Saga só vão para o log. Mensageria:Transporte escolhe o
        /// broker: "RabbitMq" (padrão) ou "InMemory" (testes, sem broker).
        ///
        /// Os eventos internos de domínio (orcamento.calculado, pagamento.aprovado...) ficam
        /// sempre no log: o que os outros serviços precisam vai pela Saga.
        /// </summary>
        public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IPublicadorEventos, PublicadorEventosLog>();

            var secao = configuration.GetSection(Secao);
            if (!secao.GetValue<bool>("Habilitada"))
            {
                services.AddScoped<IPublicadorSaga, PublicadorSagaLog>();
                return services;
            }

            var emMemoria = string.Equals(secao["Transporte"], "InMemory", StringComparison.OrdinalIgnoreCase);

            services.AddScoped<IPublicadorSaga, PublicadorSagaMassTransit>();

            services.AddMassTransit(x =>
            {
                x.AddConsumer<GerarOrcamentoConsumer>().Endpoint(e => e.Name = FilasBilling.GerarOrcamento);
                x.AddConsumer<CancelarOrcamentoConsumer>().Endpoint(e => e.Name = FilasBilling.CancelarOrcamento);
                x.AddConsumer<EstornarPagamentoConsumer>().Endpoint(e => e.Name = FilasBilling.EstornarPagamento);

                x.AddConfigureEndpointsCallback((contexto, nome, endpoint) =>
                {
                    // Cada fila recebe um tipo só, então aceita JSON sem cabeçalho de tipo
                    // (publicado por quem não usa MassTransit).
                    if (FilasBilling.Todas.Contains(nome))
                        endpoint.UseRawJsonDeserializer(RawSerializerOptions.AnyMessageType | RawSerializerOptions.CopyHeaders, isDefault: true);

                    // Erro técnico (banco fora, Mercado Pago fora): tenta de novo com intervalo
                    // crescente; esgotado, vai para a fila _error. Erro de negócio já virou
                    // OrcamentoFalhou no serviço e não chega aqui.
                    endpoint.UseMessageRetry(r =>
                    {
                        r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(3));
                        r.Ignore<KeyNotFoundException>();
                    });

                    // As respostas publicadas no consumidor só saem se ele terminar sem erro.
                    endpoint.UseInMemoryOutbox(contexto);
                });

                if (emMemoria)
                {
                    x.UsingInMemory((contexto, cfg) =>
                    {
                        ConfigurarSerializacao(cfg);
                        cfg.ConfigureEndpoints(contexto);
                    });
                }
                else
                {
                    x.UsingRabbitMq((contexto, cfg) =>
                    {
                        var rabbit = configuration.GetSection("RabbitMq");
                        cfg.Host(rabbit["Host"] ?? "localhost", rabbit.GetValue<ushort?>("Porta") ?? 5672, rabbit["VirtualHost"] ?? "/", h =>
                        {
                            h.Username(rabbit["Usuario"] ?? "guest");
                            h.Password(rabbit["Senha"] ?? "guest");
                        });

                        ConfigurarSerializacao(cfg);
                        cfg.ConfigureEndpoints(contexto);
                    });
                }
            });

            return services;
        }

        /// <summary>
        /// JSON puro, sem o envelope do MassTransit, como define o contrato (igual ao OS Service
        /// e à Execução). O exchange de cada mensagem continua "MechanicLtda.Contratos:Nome".
        /// </summary>
        private static void ConfigurarSerializacao(IBusFactoryConfigurator cfg)
        {
            cfg.UseRawJsonSerializer(RawSerializerOptions.AddTransportHeaders | RawSerializerOptions.CopyHeaders, isDefault: true);
            cfg.UseRawJsonDeserializer(RawSerializerOptions.Default | RawSerializerOptions.CopyHeaders, isDefault: true);
        }
    }
}
