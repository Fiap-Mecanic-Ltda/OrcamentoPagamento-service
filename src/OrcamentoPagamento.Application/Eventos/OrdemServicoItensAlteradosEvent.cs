using OrcamentoPagamento.Domain.Enums;

namespace OrcamentoPagamento.Application.Eventos
{
    /// <summary>
    /// Evento publicado pelo serviço de Ordem de Serviço (hoje, o monólito MechanicLtda)
    /// sempre que os itens de uma OS são adicionados, alterados ou removidos.
    ///
    /// O payload é deliberadamente auto-contido: traz os itens já classificados e os dados
    /// de cliente/veículo. Assim o microsserviço de orçamento não precisa chamar nenhum
    /// outro serviço para calcular nem para emitir o PDF — o que elimina o acoplamento em
    /// runtime que faria a indisponibilidade da Oficina derrubar o orçamento também.
    ///
    /// Contrato versionado em docs/adr/ADR-002-contrato-de-eventos.md.
    /// </summary>
    public class OrdemServicoItensAlteradosEvent
    {
        /// <summary>Identificador único da ocorrência — base da idempotência no consumo.</summary>
        public Guid EventoId { get; set; }

        /// <summary>Tipo do evento, para roteamento. Esperado: ordem-servico.itens-alterados.</summary>
        public string TipoEvento { get; set; } = TipoEsperado;

        /// <summary>Momento em que a alteração aconteceu na origem (UTC).</summary>
        public DateTime OcorridoEm { get; set; }

        /// <summary>Propagado do X-Correlation-Id da requisição que originou a alteração.</summary>
        public string? CorrelationId { get; set; }

        public int OrdemServicoId { get; set; }

        public OrdemServicoDadosEvent? OrdemServico { get; set; }

        public List<ItemEvent> Itens { get; set; } = new();

        public const string TipoEsperado = "ordem-servico.itens-alterados";
    }

    public class ItemEvent
    {
        /// <summary>Id do item na OS de origem.</summary>
        public int? ItemId { get; set; }

        public string Descricao { get; set; } = string.Empty;

        /// <summary>
        /// Peca, Insumo ou Servico. Quem publica resolve a categoria: no monólito ela vem do
        /// TipoEstoque do item que consumiu estoque, ou é Servico quando o item referencia
        /// um ServicoOficina / tem apenas descrição de mão de obra.
        /// </summary>
        public CategoriaItem Categoria { get; set; }

        public int Quantidade { get; set; }

        public decimal ValorUnitario { get; set; }
    }

    public class OrdemServicoDadosEvent
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
