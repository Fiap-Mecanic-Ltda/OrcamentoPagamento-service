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

        /// <summary>Situação na Saga da OS: Aberto, Aprovado, Pago, Recusado ou Cancelado.</summary>
        public StatusOrcamento Status { get; set; }

        /// <summary>Nome do status, para a tela não precisar conhecer o enum.</summary>
        public string StatusDescricao { get; set; } = string.Empty;

        public string? MotivoStatus { get; set; }

        /// <summary>Id da Saga da OS que gerou o orçamento. Nulo quando veio da rota manual.</summary>
        public Guid? CorrelationId { get; set; }

        public List<ItemOrcamentoDto> Itens { get; set; } = new();
    }

    /// <summary>Corpo opcional da recusa do orçamento pelo cliente.</summary>
    public class OrcamentoRecusaDto
    {
        public string? Motivo { get; set; }
    }

    /// <summary>Desfecho de aprovar/recusar, para o controller escolher o status HTTP.</summary>
    public enum ResultadoDecisaoOrcamento
    {
        Ok,
        NaoEncontrado,
        Indisponivel,
        Erro
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
