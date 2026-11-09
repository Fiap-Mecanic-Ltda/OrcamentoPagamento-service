using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas.Base;

namespace OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas
{
    public class PagamentoConfig : ConfiguracaoContextoBase
    {
        public override void Configurar(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Pagamento>(entity =>
            {
                entity.ToTable("Pagamentos");

                entity.Property(e => e.Valor)
                      .IsRequired()
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.Status)
                      .IsRequired()
                      .HasConversion<int>();

                entity.Property(e => e.StatusDetalhe).HasMaxLength(100);
                entity.Property(e => e.Provedor).IsRequired().HasMaxLength(30);
                entity.Property(e => e.IdExterno).HasMaxLength(50);
                entity.Property(e => e.MetodoPagamento).HasMaxLength(30);
                entity.Property(e => e.MotivoCancelamento).HasMaxLength(50);

                entity.Property(e => e.CriadoEm).IsRequired();
                entity.Property(e => e.AtualizadoEm).IsRequired();

                // Idempotência de entrada: o banco é a última linha de defesa contra
                // cobrança dupla, mesmo se duas requisições chegarem ao mesmo tempo.
                entity.HasIndex(e => e.RequisicaoId).IsUnique();

                // O webhook chega com o id do provedor; é por aqui que localizamos o
                // pagamento. Único para não haver dois registros do mesmo pagamento —
                // filtrado porque IdExterno é nulo até a criação retornar.
                entity.HasIndex(e => e.IdExterno)
                      .IsUnique()
                      .HasFilter("[IdExterno] IS NOT NULL");

                entity.HasIndex(e => e.OrcamentoId);
                entity.HasIndex(e => e.OrdemServicoId);

                // Apagar o orçamento apaga os pagamentos dele. Sem cascata, a remoção de
                // orçamento quebraria com erro de FK.
                entity.HasOne(e => e.Orcamento)
                      .WithMany(o => o.Pagamentos)
                      .HasForeignKey(e => e.OrcamentoId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
