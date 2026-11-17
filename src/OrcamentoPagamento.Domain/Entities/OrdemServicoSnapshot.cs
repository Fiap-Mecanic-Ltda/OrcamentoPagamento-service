using OrcamentoPagamento.Domain.Entities.Base;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrcamentoPagamento.Domain.Entities
{
    /// <summary>
    /// Dados da OS, do cliente e do veículo no momento em que o orçamento foi calculado.
    /// Existe porque o PDF precisa desses campos e o microsserviço não tem acesso aos
    /// bancos de Cadastro e Oficina — eles vêm no evento e ficam congelados aqui.
    /// Também é o que garante que um PDF reemitido reproduza o orçamento como ele era.
    /// </summary>
    public class OrdemServicoSnapshot : Entity<int>
    {
        [Required]
        public int OrcamentoId { get; set; }

        [ForeignKey(nameof(OrcamentoId))]
        public Orcamento Orcamento { get; set; } = null!;

        [MaxLength(1000)]
        public string? DescricaoProblema { get; set; }

        [MaxLength(50)]
        public string? StatusOrdemServico { get; set; }

        [MaxLength(200)]
        public string? ClienteNome { get; set; }

        [MaxLength(200)]
        public string? ClienteEmail { get; set; }

        [MaxLength(30)]
        public string? ClienteTelefone { get; set; }

        [MaxLength(100)]
        public string? VeiculoMarca { get; set; }

        [MaxLength(100)]
        public string? VeiculoModelo { get; set; }

        [MaxLength(10)]
        public string? VeiculoPlaca { get; set; }

        public int? VeiculoAno { get; set; }

        [Required]
        public DateTime AtualizadoEm { get; set; }
    }
}
