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

        /// <summary>
        /// Processa o evento de itens alterados. Usado pelo consumidor da fila e pelo
        /// endpoint de recálculo — mesma regra, duas portas de entrada.
        /// </summary>
        Task<ResponseDto<OrcamentoDto?>> RecalcularAsync(OrdemServicoItensAlteradosEvent evento, CancellationToken cancellationToken = default);
    }
}
