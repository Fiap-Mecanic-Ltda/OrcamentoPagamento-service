using OrcamentoPagamento.Domain.Enums;

namespace OrcamentoPagamento.Application.DTOs
{
    public class OrcamentoDto
    {
        public int Id { get; set; }
        public int OrdemServicoId { get; set; }
        public decimal ValorTotalPecas { get; set; }
        public decimal ValorTotalInsumos { get; set; }
        public decimal ValorTotalServicos { get; set; }
        public decimal ValorTotalGeral { get; set; }
        public DateTime DataGeracao { get; set; }
        public DateTime? Validade { get; set; }
        public bool AjustadoManualmente { get; set; }
        public DateTime? AjustadoEm { get; set; }
        public List<ItemOrcamentoDto> Itens { get; set; } = new();
    }

    public class ItemOrcamentoDto
    {
        public int Id { get; set; }
        public int? ItemOrigemId { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public CategoriaItem Categoria { get; set; }
        public int Quantidade { get; set; }
        public decimal ValorUnitario { get; set; }
        public decimal ValorTotal { get; set; }
    }

    public class OrcamentoUpdateDto
    {
        public decimal ValorTotalPecas { get; set; }
        public decimal ValorTotalInsumos { get; set; }
        public decimal ValorTotalServicos { get; set; }
        public DateTime? Validade { get; set; }
    }

    public class OrcamentoPdfDto
    {
        public int Id { get; set; }
        public int OrdemServicoId { get; set; }
        public DateTime DataGeracao { get; set; }
        public DateTime? Validade { get; set; }

        public decimal ValorTotalPecas { get; set; }
        public decimal ValorTotalInsumos { get; set; }
        public decimal ValorTotalServicos { get; set; }
        public decimal ValorTotalGeral { get; set; }

        // Ordem de Serviço (snapshot local)
        public string? DescricaoProblema { get; set; }
        public string? StatusOrdemServico { get; set; }

        // Cliente (snapshot local)
        public string? ClienteNome { get; set; }
        public string? ClienteEmail { get; set; }
        public string? ClienteTelefone { get; set; }

        // Veículo (snapshot local)
        public string? VeiculoMarca { get; set; }
        public string? VeiculoModelo { get; set; }
        public string? VeiculoPlaca { get; set; }
        public int? VeiculoAno { get; set; }

        public List<ItemOrcamentoDto> Itens { get; set; } = new();
    }
}
