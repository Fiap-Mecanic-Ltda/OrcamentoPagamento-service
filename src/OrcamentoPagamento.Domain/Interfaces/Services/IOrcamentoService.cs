using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;

namespace OrcamentoPagamento.Domain.Interfaces.Services
{
    public interface IOrcamentoService
    {
        /// <summary>
        /// Cria ou atualiza o orçamento da OS a partir dos itens recebidos.
        /// Retorna null quando o evento é descartado por idempotência.
        /// </summary>
        Task<Orcamento?> CalcularAsync(CalcularOrcamentoCommand command, CancellationToken cancellationToken = default);

        Task<Orcamento> AtualizarManualAsync(int id, decimal valorTotalPecas, decimal valorTotalInsumos, decimal valorTotalServicos, DateTime? validade, CancellationToken cancellationToken = default);

        Task<Orcamento?> ObterPorIdAsync(string id);

        Task<Orcamento?> ObterPorOrdemServicoIdAsync(int ordemServicoId);

        Task<Orcamento?> ObterComDetalhesAsync(int id);

        Task RemoverAsync(string id, CancellationToken cancellationToken = default);
    }
}
