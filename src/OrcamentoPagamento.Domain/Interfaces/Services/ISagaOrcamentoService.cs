using MechanicLtda.Contratos;
using OrcamentoPagamento.Domain.Entities;

namespace OrcamentoPagamento.Domain.Interfaces.Services
{
    /// <summary>
    /// Participação do Orçamento/Pagamento (Billing) na Saga orquestrada pelo OS Service
    /// (docs/contratos-eventos.md). Os comandos chegam pelo RabbitMQ; aprovação e recusa, pela
    /// API (o cliente decide pelo Web). O pagamento confirmado sai do PagamentoService.
    /// </summary>
    public interface ISagaOrcamentoService
    {
        /// <summary>Gera o orçamento e responde OrcamentoGerado, ou OrcamentoFalhou.</summary>
        Task TratarAsync(GerarOrcamento comando, CancellationToken cancellationToken = default);

        /// <summary>Compensação: cancela o orçamento e os pagamentos em aberto; responde OrcamentoCancelado.</summary>
        Task TratarAsync(CancelarOrcamento comando, CancellationToken cancellationToken = default);

        /// <summary>Compensação: devolve o pagamento no provedor; responde PagamentoEstornado.</summary>
        Task TratarAsync(EstornarPagamento comando, CancellationToken cancellationToken = default);

        /// <summary>Aprovação sem pagamento (informativa): publica OrcamentoAprovado.</summary>
        Task<Orcamento> AprovarAsync(int orcamentoId, CancellationToken cancellationToken = default);

        /// <summary>Recusa do cliente: encerra o orçamento e publica OrcamentoRecusado.</summary>
        Task<Orcamento> RecusarAsync(int orcamentoId, string? motivo, CancellationToken cancellationToken = default);
    }
}
