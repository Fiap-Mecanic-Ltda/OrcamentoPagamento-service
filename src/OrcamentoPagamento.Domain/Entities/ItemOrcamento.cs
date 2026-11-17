using OrcamentoPagamento.Domain.Entities.Base;
using OrcamentoPagamento.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrcamentoPagamento.Domain.Entities
{
    /// <summary>
    /// Cópia local do item que originou o orçamento. O microsserviço não consulta a OS
    /// para recalcular nem para emitir o PDF — ele guarda o que recebeu no evento.
    /// </summary>
    public class ItemOrcamento : Entity<int>
    {
        [Required]
        public int OrcamentoId { get; set; }

        [ForeignKey(nameof(OrcamentoId))]
        public Orcamento Orcamento { get; set; } = null!;

        /// <summary>Id do item na Ordem de Serviço de origem. Sem FK: outro banco, outro serviço.</summary>
        public int? ItemOrigemId { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Descricao { get; set; } = string.Empty;

        [Required]
        public CategoriaItem Categoria { get; set; }

        [Required]
        public int Quantidade { get; set; }

        [Required]
        public decimal ValorUnitario { get; set; }

        public decimal ValorTotal { get; set; }

        public void CalcularValorTotal()
        {
            ValorTotal = Quantidade * ValorUnitario;
        }
    }
}
