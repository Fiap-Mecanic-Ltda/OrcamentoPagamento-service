using MechanicLtda.Contratos;

namespace OrcamentoPagamento.Domain.Interfaces.Messaging
{
    /// <summary>
    /// Publica as mensagens da Saga da OS (docs/contratos-eventos.md) no RabbitMQ, no exchange
    /// fanout com o nome da mensagem (MechanicLtda.Contratos:&lt;Mensagem&gt;). Com a mensageria
    /// desligada, a implementação só registra em log.
    /// </summary>
    public interface IPublicadorSaga
    {
        Task PublicarAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : MensagemSaga;
    }
}
