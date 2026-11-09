using Microsoft.Extensions.Logging;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using System.Text.Json;

namespace OrcamentoPagamento.Infrastructure.Messaging
{
    /// <summary>
    /// Publicador usado quando a mensageria está desligada (Messaging:Habilitado = false):
    /// execução local sem AWS, e os testes de integração. Registra o evento e segue.
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
                "Mensageria desligada. Evento {TipoEvento} nao publicado. Payload: {Payload}",
                tipoEvento, JsonSerializer.Serialize(payload));

            return Task.CompletedTask;
        }
    }
}
