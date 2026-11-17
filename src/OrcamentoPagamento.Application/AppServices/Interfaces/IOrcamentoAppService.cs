using OrcamentoPagamento.Application.DTOs;
using OrcamentoPagamento.Application.Eventos;

namespace OrcamentoPagamento.Application.AppServices.Interfaces
{
    public interface IOrcamentoAppService
    {
        Task<ResponseDto<OrcamentoDto>> ObterPorIdAsync(string id);

        Task<ResponseDto<OrcamentoDto>> ObterPorOrdemServicoIdAsync(int ordemServicoId);

        Task<ResponseDto<OrcamentoDto>> AtualizarManualAsync(string id, OrcamentoUpdateDto dto, CancellationToken cancellationToken = default);

        Task<ResponseDto<bool>> RemoverAsync(string id, CancellationToken cancellationToken = default);

        Task<ResponseDto<byte[]>> ExportarPdfAsync(int id);

        /// <summary>Aprovação sem pagamento (informativa para a Saga da OS).</summary>
        Task<(ResultadoDecisaoOrcamento Resultado, ResponseDto<OrcamentoDto> Resposta)> AprovarAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>Recusa do cliente: encerra o orçamento e avisa a Saga da OS.</summary>
        Task<(ResultadoDecisaoOrcamento Resultado, ResponseDto<OrcamentoDto> Resposta)> RecusarAsync(int id, string? motivo, CancellationToken cancellationToken = default);

        /// <summary>
        /// Recálculo manual pelo payload de itens (rota POST /api/orcamento/recalcular), fora
        /// da Saga. A Saga usa o mesmo cálculo pelo GerarOrcamento (SagaOrcamentoService).
        /// </summary>
        Task<ResponseDto<OrcamentoDto?>> RecalcularAsync(OrdemServicoItensAlteradosEvent evento, CancellationToken cancellationToken = default);
    }
}
