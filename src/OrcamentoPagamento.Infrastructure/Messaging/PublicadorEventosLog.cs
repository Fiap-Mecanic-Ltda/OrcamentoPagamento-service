using Microsoft.Extensions.Logging;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using System.Text.Json;

namespace OrcamentoPagamento.Infrastructure.Messaging
{
    /// <summary>
    /// Eventos internos de domínio (orcamento.calculado, pagamento.aprovado...). Não saem do
    /// serviço: ficam no log estruturado, para auditoria e rastreio no New Relic. O que outros
    /// serviços precisam saber vai pela Saga (IPublicadorSaga, RabbitMQ).
    /// </summary>
    public class PublicadorEventosLog : IPublicadorEventos
    {
        private readonly ILogger<PublicadorEventosLog> _logger;

        public PublicadorEventosLog(ILogger<PublicadorEventosLog> logger)
        {
            _logger = logger;
        }

        public Task PublicarAsync(string tipoEvento, object payload, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Evento de dominio {TipoEvento}: {Payload}",
                tipoEvento, JsonSerializer.Serialize(payload));

            return Task.CompletedTask;
        }
    }
}
