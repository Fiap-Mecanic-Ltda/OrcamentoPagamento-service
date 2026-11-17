using MassTransit;
using MechanicLtda.Contratos;
using Microsoft.Extensions.Logging;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using OrcamentoPagamento.Domain.Messaging;
using System.Text.Json;

namespace OrcamentoPagamento.Infrastructure.Messaging
{
    /// <summary>
    /// Publica no exchange da mensagem (MechanicLtda.Contratos:&lt;Nome&gt;), com os cabeçalhos
    /// correlation-id e x-correlation-id do contrato. Dentro de um consumidor, o
    /// IPublishEndpoint do escopo é o próprio ConsumeContext e o outbox em memória segura a
    /// publicação até o consumidor terminar sem erro. Numa requisição HTTP (pagamento,
    /// aprovação, recusa), publica direto no broker.
    /// </summary>
    public class PublicadorSagaMassTransit : IPublicadorSaga
    {
        private readonly IPublishEndpoint _publish;
        private readonly ILogger<PublicadorSagaMassTransit> _logger;

        public PublicadorSagaMassTransit(IPublishEndpoint publish, ILogger<PublicadorSagaMassTransit> logger)
        {
            _publish = publish;
            _logger = logger;
        }

        public async Task PublicarAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : MensagemSaga
        {
            await _publish.Publish(mensagem, contexto =>
            {
                contexto.CorrelationId = mensagem.CorrelationId;
                contexto.Headers.Set(ContextoCorrelacao.CabecalhoCorrelationId, mensagem.CorrelationId.ToString());

                var requisicao = ContextoCorrelacao.CorrelationIdRequisicao;
                if (!string.IsNullOrWhiteSpace(requisicao))
                    contexto.Headers.Set(ContextoCorrelacao.CabecalhoRequisicao, requisicao);
            }, cancellationToken);

            _logger.LogInformation(
                "{Mensagem} publicado para a OS {OrdemServicoId} (Saga {Saga}).",
                typeof(T).Name, mensagem.OrdemServicoId, mensagem.CorrelationId);
        }
    }

    /// <summary>
    /// Mensageria desligada (Mensageria:Habilitada=false): nada vai para o RabbitMQ e o serviço
    /// nem conecta nele. A mensagem fica no log, para conferência local e nos testes.
    /// </summary>
    public class PublicadorSagaLog : IPublicadorSaga
    {
        private readonly ILogger<PublicadorSagaLog> _logger;

        public PublicadorSagaLog(ILogger<PublicadorSagaLog> logger) => _logger = logger;

        public Task PublicarAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : MensagemSaga
        {
            _logger.LogInformation(
                "Mensageria desligada. {Mensagem} nao publicado: {Payload}",
                typeof(T).Name, JsonSerializer.Serialize(mensagem));
            return Task.CompletedTask;
        }
    }
}
