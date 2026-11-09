using OrcamentoPagamento.Domain.Entities.Base;
using OrcamentoPagamento.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace OrcamentoPagamento.Domain.Entities
{
    /// <summary>
    /// Agregado raiz do microsserviço. Vinculado a uma Ordem de Serviço apenas por Id —
    /// a OS vive no banco de outro serviço, então não há chave estrangeira.
    /// </summary>
    public class Orcamento : Entity<int>
    {
        /// <summary>Id da OS no serviço de Oficina. Único: uma OS tem um orçamento.</summary>
        [Required]
        public int OrdemServicoId { get; set; }

        [Required]
        public decimal ValorTotalPecas { get; set; }

        [Required]
        public decimal ValorTotalInsumos { get; set; }

        [Required]
        public decimal ValorTotalServicos { get; set; }

        [Required]
        public decimal ValorTotalGeral { get; set; }

        [Required]
        public DateTime DataGeracao { get; set; }

        public DateTime? Validade { get; set; }

        /// <summary>Marca que houve ajuste manual de valores. Auditoria apenas — um novo
        /// evento de itens alterados recalcula e sobrescreve, igual ao monólito.</summary>
        public bool AjustadoManualmente { get; set; }

        public DateTime? AjustadoEm { get; set; }

        // ─── Idempotência do consumo de eventos ─────────────────────────────────
        // O SQS entrega ao menos uma vez e sem garantia de ordem entre mensagens de
        // OSs diferentes. Guardar o último evento aplicado permite descartar
        // reentrega (mesmo EventoId) e evento atrasado (OcorridoEm mais antigo).

        public Guid? UltimoEventoId { get; set; }

        public DateTime? UltimoEventoEm { get; set; }

        public ICollection<ItemOrcamento> Itens { get; set; } = new List<ItemOrcamento>();

        /// <summary>Tentativas de pagamento deste orcamento. Ver docs/pagamento.md.</summary>
        public ICollection<Pagamento> Pagamentos { get; set; } = new List<Pagamento>();

        public OrdemServicoSnapshot? OrdemServicoSnapshot { get; set; }

        /// <summary>
        /// Recalcula os totais a partir dos itens atuais, separando por categoria.
        /// </summary>
        public void RecalcularTotais()
        {
            foreach (var item in Itens)
                item.CalcularValorTotal();

            ValorTotalPecas = Itens.Where(i => i.Categoria == CategoriaItem.Peca).Sum(i => i.ValorTotal);
            ValorTotalInsumos = Itens.Where(i => i.Categoria == CategoriaItem.Insumo).Sum(i => i.ValorTotal);
            ValorTotalServicos = Itens.Where(i => i.Categoria == CategoriaItem.Servico).Sum(i => i.ValorTotal);

            // Soma dos itens, e não das três categorias: se amanhã surgir uma categoria
            // nova, o total geral continua fechando com os itens recebidos.
            ValorTotalGeral = Itens.Sum(i => i.ValorTotal);
        }

        /// <summary>
        /// Indica que o evento recebido já foi aplicado ou é mais antigo que o último
        /// aplicado — nos dois casos deve ser descartado sem erro.
        /// </summary>
        public bool DeveDescartarEvento(Guid eventoId, DateTime ocorridoEm)
        {
            if (UltimoEventoId == eventoId)
                return true;

            return UltimoEventoEm.HasValue && ocorridoEm < UltimoEventoEm.Value;
        }
    }
}
