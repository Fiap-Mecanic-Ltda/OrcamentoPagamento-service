using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrcamentoPagamento.API.Authorization;
using OrcamentoPagamento.API.ViewModels;
using OrcamentoPagamento.Application.AppServices.Interfaces;
using OrcamentoPagamento.Application.DTOs;
using OrcamentoPagamento.Application.Eventos;

namespace OrcamentoPagamento.API.Controllers
{
    /// <summary>
    /// Rotas consumidas pelo host Web (via API Gateway). O envelope de resposta é o mesmo
    /// ResponseDto do monólito, para que as views não precisem mudar de formato.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = Roles.Admin)]
    public class OrcamentoController : ControllerBase
    {
        private readonly IOrcamentoAppService _orcamentoAppService;

        public OrcamentoController(IOrcamentoAppService orcamentoAppService)
        {
            _orcamentoAppService = orcamentoAppService;
        }

        /// <summary>Obtém um orçamento pelo seu Id.</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> ObterPorId(int id)
        {
            var response = await _orcamentoAppService.ObterPorIdAsync(id.ToString());
            return response.hasErrors ? NotFound(response) : Ok(response);
        }

        /// <summary>Obtém o orçamento vinculado a uma Ordem de Serviço.</summary>
        [HttpGet("ordem-servico/{ordemServicoId:int}")]
        public async Task<IActionResult> ObterPorOrdemServico(int ordemServicoId)
        {
            var response = await _orcamentoAppService.ObterPorOrdemServicoIdAsync(ordemServicoId);
            return response.hasErrors ? NotFound(response) : Ok(response);
        }

        /// <summary>Realiza ajuste manual de valores no orçamento.</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> AtualizarManual(int id, [FromBody] OrcamentoUpdateViewModel viewModel, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var dto = new OrcamentoUpdateDto
            {
                ValorTotalPecas = viewModel.ValorTotalPecas,
                ValorTotalInsumos = viewModel.ValorTotalInsumos,
                ValorTotalServicos = viewModel.ValorTotalServicos,
                Validade = viewModel.Validade
            };

            var response = await _orcamentoAppService.AtualizarManualAsync(id.ToString(), dto, cancellationToken);
            return response.hasErrors ? BadRequest(response) : Ok(response);
        }

        /// <summary>Remove o orçamento (geralmente ao cancelar a OS).</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Remover(int id, CancellationToken cancellationToken)
        {
            var response = await _orcamentoAppService.RemoverAsync(id.ToString(), cancellationToken);
            return response.hasErrors ? BadRequest(response) : Ok(response);
        }

        /// <summary>Exporta o orçamento como relatório em PDF.</summary>
        [HttpGet("{id:int}/pdf")]
        public async Task<IActionResult> ExportarPdf(int id)
        {
            var response = await _orcamentoAppService.ExportarPdfAsync(id);

            if (response.hasErrors)
                return NotFound(response);

            return File(
                response.getResponse,
                "application/pdf",
                $"orcamento-{id:D6}.pdf");
        }

        /// <summary>
        /// Recalcula o orçamento a partir do mesmo payload do evento da fila.
        ///
        /// Existe para três situações: reprocessar uma mensagem que caiu na DLQ, operar com
        /// a mensageria desligada, e permitir que o serviço de Ordem de Serviço force o
        /// cálculo de forma síncrona quando precisar do orçamento na mesma requisição.
        /// A idempotência é a mesma do consumo: EventoId repetido não recalcula.
        /// </summary>
        [HttpPost("recalcular")]
        public async Task<IActionResult> Recalcular([FromBody] OrdemServicoItensAlteradosEvent evento, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (evento.OrdemServicoId <= 0)
                return BadRequest(new { erro = "OrdemServicoId e obrigatorio." });

            var response = await _orcamentoAppService.RecalcularAsync(evento, cancellationToken);

            if (response.hasErrors)
                return BadRequest(response);

            // Sem conteúdo = evento já aplicado antes. O chamador não deve tratar como erro.
            return response.getResponse is null ? NoContent() : Ok(response);
        }
    }
}
