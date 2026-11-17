using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Interfaces.Repositories;
using OrcamentoPagamento.Infrastructure.Repositories.Base;

namespace OrcamentoPagamento.Infrastructure.Repositories
{
    public class OrcamentoRepository : Repository<Orcamento>, IOrcamentoRepository
    {
        public OrcamentoRepository(BancoOrcamentoContext context) : base(context) { }

        public override async Task<Orcamento?> ObterPorIdAsync(string id)
        {
            if (!int.TryParse(id, out var orcamentoId))
                return null;

            return await _dbSet
                .Include(o => o.Itens)
                .Include(o => o.OrdemServicoSnapshot)
                .FirstOrDefaultAsync(o => o.Id == orcamentoId);
        }

        /// <summary>
        /// Rastreado de propósito: é a instância que o cálculo altera, limpando e
        /// recriando os itens. Com os filhos carregados, o EF apaga os órfãos.
        /// </summary>
        public async Task<Orcamento?> ObterPorOrdemServicoIdAsync(int ordemServicoId)
        {
            return await _dbSet
                .Include(o => o.Itens)
                .Include(o => o.OrdemServicoSnapshot)
                .FirstOrDefaultAsync(o => o.OrdemServicoId == ordemServicoId);
        }

        public async Task<Orcamento?> ObterComDetalhesAsync(int id)
        {
            return await _dbSet
                .AsNoTracking()
                .Include(o => o.Itens)
                .Include(o => o.OrdemServicoSnapshot)
                .FirstOrDefaultAsync(o => o.Id == id);
        }
    }
}
