using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas.Base;

namespace OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas
{
    public class ItemOrcamentoConfig : ConfiguracaoContextoBase
    {
        public override void Configurar(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ItemOrcamento>(entity =>
            {
                entity.ToTable("ItensOrcamento");

                entity.Property(e => e.Descricao)
                      .IsRequired()
                      .HasMaxLength(1000);

                entity.Property(e => e.Categoria)
                      .IsRequired()
                      .HasConversion<int>();

                entity.Property(e => e.Quantidade)
                      .IsRequired();

                entity.Property(e => e.ValorUnitario)
                      .IsRequired()
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.ValorTotal)
                      .IsRequired()
                      .HasColumnType("decimal(18,2)");

                entity.HasIndex(e => e.OrcamentoId);
            });
        }
    }
}
