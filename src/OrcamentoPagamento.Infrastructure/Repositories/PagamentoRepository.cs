using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Enums;
using OrcamentoPagamento.Domain.Interfaces.Repositories;
using OrcamentoPagamento.Infrastructure.Repositories.Base;

namespace OrcamentoPagamento.Infrastructure.Repositories
{
    public class PagamentoRepository : Repository<Pagamento>, IPagamentoRepository
    {
        /// <summary>Status em que o pagamento ainda pode ser concluído ou abandonado.</summary>
        private static readonly StatusPagamento[] StatusEmAberto =
        {
            StatusPagamento.Pendente,
            StatusPagamento.EmProcessamento,
            StatusPagamento.Autorizado
        };

        public PagamentoRepository(BancoOrcamentoContext context) : base(context) { }

        public override async Task<Pagamento?> ObterPorIdAsync(string id)
        {
            if (!int.TryParse(id, out var pagamentoId))
                return null;

            return await _dbSet.FirstOrDefaultAsync(p => p.Id == pagamentoId);
        }

        public async Task<Pagamento?> ObterPorRequisicaoIdAsync(Guid requisicaoId)
        {
            return await _dbSet
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.RequisicaoId == requisicaoId);
        }

        public async Task<Pagamento?> ObterPorIdExternoAsync(string idExterno)
        {
            return await _dbSet.FirstOrDefaultAsync(p => p.IdExterno == idExterno);
        }

        public async Task<IEnumerable<Pagamento>> ObterPorOrcamentoIdAsync(int orcamentoId)
        {
            return await _dbSet
                .AsNoTracking()
                .Where(p => p.OrcamentoId == orcamentoId)
                .OrderByDescending(p => p.CriadoEm)
                .ToListAsync();
        }

        public async Task<IEnumerable<Pagamento>> ObterPorOrdemServicoIdAsync(int ordemServicoId)
        {
            return await _dbSet
                .AsNoTracking()
                .Where(p => p.OrdemServicoId == ordemServicoId)
                .OrderByDescending(p => p.CriadoEm)
                .ToListAsync();
        }

        public async Task<Pagamento?> ObterAprovadoPorOrcamentoIdAsync(int orcamentoId)
        {
            return await _dbSet
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.OrcamentoId == orcamentoId && p.Status == StatusPagamento.Aprovado);
        }

        /// <summary>Rastreado: é a instância que o cancelamento altera.</summary>
        public async Task<IEnumerable<Pagamento>> ObterEmAbertoPorOrcamentoIdAsync(int orcamentoId)
        {
            return await _dbSet
                .Where(p => p.OrcamentoId == orcamentoId && StatusEmAberto.Contains(p.Status))
                .ToListAsync();
        }
    }
}
