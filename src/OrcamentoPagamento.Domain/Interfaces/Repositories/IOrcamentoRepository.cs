using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Interfaces.Repositories.Base;

namespace OrcamentoPagamento.Domain.Interfaces.Repositories
{
    public interface IOrcamentoRepository : IRepository<Orcamento>
    {
        /// <summary>Orçamento da OS, com itens e snapshot carregados e rastreados.</summary>
        Task<Orcamento?> ObterPorOrdemServicoIdAsync(int ordemServicoId);

        /// <summary>Orçamento com itens e snapshot — usado pelo PDF.</summary>
        Task<Orcamento?> ObterComDetalhesAsync(int id);
    }
}
