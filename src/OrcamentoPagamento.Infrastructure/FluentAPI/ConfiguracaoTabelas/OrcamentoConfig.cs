using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas.Base;

namespace OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas
{
    public class OrcamentoConfig : ConfiguracaoContextoBase
    {
        public override void Configurar(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Orcamento>(entity =>
            {
                entity.ToTable("Orcamentos");

                entity.Property(e => e.ValorTotalPecas)
                      .IsRequired()
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.ValorTotalInsumos)
                      .IsRequired()
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.ValorTotalServicos)
                      .IsRequired()
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.ValorTotalGeral)
                      .IsRequired()
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.DataGeracao)
                      .IsRequired();

                // Sem FK para a OS: ela vive no banco do serviço de Oficina. O índice
                // único garante a regra "uma OS, um orçamento" dentro deste serviço.
                entity.HasIndex(e => e.OrdemServicoId)
                      .IsUnique();

                entity.HasMany(e => e.Itens)
                      .WithOne(i => i.Orcamento)
                      .HasForeignKey(i => i.OrcamentoId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.OrdemServicoSnapshot)
                      .WithOne(s => s.Orcamento)
                      .HasForeignKey<OrdemServicoSnapshot>(s => s.OrcamentoId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
