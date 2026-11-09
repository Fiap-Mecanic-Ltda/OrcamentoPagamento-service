using OrcamentoPagamento.Domain.Enums;

namespace OrcamentoPagamento.Domain.Commands
{
    /// <summary>
    /// Entrada do cálculo do orçamento. É o mesmo contrato para o consumo da fila e para
    /// o endpoint de recálculo manual — o domínio não sabe de onde o pedido veio.
    /// </summary>
    public class CalcularOrcamentoCommand
    {
        public Guid EventoId { get; set; } = Guid.NewGuid();

        public DateTime OcorridoEm { get; set; } = DateTime.UtcNow;

        public int OrdemServicoId { get; set; }

        public int DiasValidade { get; set; } = 30;

        public IReadOnlyCollection<ItemCalculoCommand> Itens { get; set; } = Array.Empty<ItemCalculoCommand>();

        public OrdemServicoDadosCommand? OrdemServico { get; set; }
    }

    public class ItemCalculoCommand
    {
        public int? ItemOrigemId { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public CategoriaItem Categoria { get; set; }
        public int Quantidade { get; set; }
        public decimal ValorUnitario { get; set; }
    }

    public class OrdemServicoDadosCommand
    {
        public string? DescricaoProblema { get; set; }
        public string? Status { get; set; }
        public string? ClienteNome { get; set; }
        public string? ClienteEmail { get; set; }
        public string? ClienteTelefone { get; set; }
        public string? VeiculoMarca { get; set; }
        public string? VeiculoModelo { get; set; }
        public string? VeiculoPlaca { get; set; }
        public int? VeiculoAno { get; set; }
    }
}
