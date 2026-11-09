using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas.Base;

namespace OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas
{
    public class OrdemServicoSnapshotConfig : ConfiguracaoContextoBase
    {
        public override void Configurar(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrdemServicoSnapshot>(entity =>
            {
                entity.ToTable("OrdemServicoSnapshots");

                entity.Property(e => e.DescricaoProblema).HasMaxLength(1000);
                entity.Property(e => e.StatusOrdemServico).HasMaxLength(50);
                entity.Property(e => e.ClienteNome).HasMaxLength(200);
                entity.Property(e => e.ClienteEmail).HasMaxLength(200);
                entity.Property(e => e.ClienteTelefone).HasMaxLength(30);
                entity.Property(e => e.VeiculoMarca).HasMaxLength(100);
                entity.Property(e => e.VeiculoModelo).HasMaxLength(100);
                entity.Property(e => e.VeiculoPlaca).HasMaxLength(10);

                entity.Property(e => e.AtualizadoEm).IsRequired();

                entity.HasIndex(e => e.OrcamentoId).IsUnique();
            });
        }
    }
}
