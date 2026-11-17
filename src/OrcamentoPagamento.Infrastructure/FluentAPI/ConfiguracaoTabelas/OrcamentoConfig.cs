using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Enums;
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

                // Situação na Saga da OS. Orçamentos anteriores à Saga ficam Aberto (1).
                entity.Property(e => e.Status)
                      .IsRequired()
                      .HasDefaultValue(StatusOrcamento.Aberto)
                      .HasSentinel((StatusOrcamento)0);

                entity.Property(e => e.MotivoStatus)
                      .HasMaxLength(500);

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
