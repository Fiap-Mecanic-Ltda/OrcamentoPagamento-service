using System.ComponentModel.DataAnnotations;

namespace OrcamentoPagamento.API.ViewModels
{
    public class OrcamentoUpdateViewModel
    {
        [Range(0, double.MaxValue, ErrorMessage = "O valor total de peças não pode ser negativo.")]
        public decimal ValorTotalPecas { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "O valor total de insumos não pode ser negativo.")]
        public decimal ValorTotalInsumos { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "O valor total de serviços não pode ser negativo.")]
        public decimal ValorTotalServicos { get; set; }

        public DateTime? Validade { get; set; }
    }
}
