namespace OrcamentoPagamento.Domain.Interfaces.Messaging
{
    /// <summary>
    /// Publica eventos de domínio para fora do serviço. A implementação de produção fala
    /// com o SNS; em testes e na execução sem AWS, um publicador que só registra em log.
    /// </summary>
    public interface IPublicadorEventos
    {
        Task PublicarAsync(string tipoEvento, object payload, CancellationToken cancellationToken = default);
    }
}
