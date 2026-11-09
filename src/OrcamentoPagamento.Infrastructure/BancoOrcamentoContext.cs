using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Infrastructure.FluentAPI;

namespace OrcamentoPagamento.Infrastructure
{
    /// <summary>
    /// Banco exclusivo do microsserviço (database per service). Não contém Cliente,
    /// Veiculo, OrdemServico nem Estoque — só o que o orçamento possui.
    /// </summary>
    public class BancoOrcamentoContext : DbContext
    {
        public BancoOrcamentoContext(DbContextOptions<BancoOrcamentoContext> options) : base(options) { }

        public DbSet<Orcamento> Orcamentos => Set<Orcamento>();
        public DbSet<ItemOrcamento> ItensOrcamento => Set<ItemOrcamento>();
        public DbSet<OrdemServicoSnapshot> OrdemServicoSnapshots => Set<OrdemServicoSnapshot>();
        public DbSet<Pagamento> Pagamentos => Set<Pagamento>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            ConfiguracaoContexto.AplicarConfiguracoes(modelBuilder);
        }
    }
}
