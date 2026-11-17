namespace OrcamentoPagamento.Domain.Interfaces.Messaging
{
    /// <summary>
    /// Eventos internos de domínio (orcamento.calculado, pagamento.aprovado...), registrados
    /// em log estruturado para auditoria. O que outros serviços precisam saber vai pela Saga
    /// (<see cref="IPublicadorSaga"/>).
    /// </summary>
    public interface IPublicadorEventos
    {
        Task PublicarAsync(string tipoEvento, object payload, CancellationToken cancellationToken = default);
    }
}
