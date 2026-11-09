using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Interfaces.Repositories.Base;

namespace OrcamentoPagamento.Domain.Interfaces.Repositories
{
    public interface IPagamentoRepository : IRepository<Pagamento>
    {
        /// <summary>Busca pela chave de idempotência de entrada.</summary>
        Task<Pagamento?> ObterPorRequisicaoIdAsync(Guid requisicaoId);

        /// <summary>Busca pelo id do pagamento no provedor — é o que o webhook traz.</summary>
        Task<Pagamento?> ObterPorIdExternoAsync(string idExterno);

        /// <summary>Histórico de tentativas de um orçamento, mais recente primeiro.</summary>
        Task<IEnumerable<Pagamento>> ObterPorOrcamentoIdAsync(int orcamentoId);

        Task<IEnumerable<Pagamento>> ObterPorOrdemServicoIdAsync(int ordemServicoId);

        /// <summary>Pagamento aprovado do orçamento, se houver. Base da regra de 409.</summary>
        Task<Pagamento?> ObterAprovadoPorOrcamentoIdAsync(int orcamentoId);

        /// <summary>Pagamentos ainda em aberto, rastreados — é o que o recálculo cancela.</summary>
        Task<IEnumerable<Pagamento>> ObterEmAbertoPorOrcamentoIdAsync(int orcamentoId);
    }
}
