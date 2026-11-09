using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrcamentoPagamento.API.Authorization;
using OrcamentoPagamento.API.ViewModels;
using OrcamentoPagamento.Application.AppServices.Interfaces;
using OrcamentoPagamento.Application.DTOs;

namespace OrcamentoPagamento.API.Controllers
{
    /// <summary>
    /// Rotas de pagamento consumidas pelo host Web (via API Gateway).
    /// Contrato completo em docs/pagamento.md.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = Roles.Admin)]
    public class PagamentoController : ControllerBase
    {
        private readonly IPagamentoAppService _pagamentoAppService;

        public PagamentoController(IPagamentoAppService pagamentoAppService)
        {
            _pagamentoAppService = pagamentoAppService;
        }

        /// <summary>
        /// Cria o pagamento a partir do token do cartão gerado no front-end.
        ///
        /// O valor **não** vem no corpo: é lido do orçamento no servidor. Recusa do provedor
        /// devolve 201 — é resultado de negócio, não erro de API.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Criar([FromBody] PagamentoCreateViewModel viewModel, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var dto = new PagamentoCreateDto
            {
                RequisicaoId = viewModel.RequisicaoId,
                OrcamentoId = viewModel.OrcamentoId,
                Token = viewModel.Token,
                PaymentMethodId = viewModel.PaymentMethodId,
                IssuerId = viewModel.IssuerId,
                Installments = viewModel.Installments,
                Descricao = viewModel.Descricao,
                Payer = new PagadorDto
                {
                    Email = viewModel.Payer.Email,
                    Identification = viewModel.Payer.Identification is null ? null : new IdentificacaoDto
                    {
                        Type = viewModel.Payer.Identification.Type,
                        Number = viewModel.Payer.Identification.Number
                    }
                }
            };

            var resposta = await _pagamentoAppService.CriarAsync(dto, cancellationToken);

            return resposta.Resultado switch
            {
                ResultadoPagamento.Criado => CreatedAtAction(
                    nameof(ObterPorId),
                    new { id = resposta.Conteudo.getResponse.Id },
                    resposta.Conteudo),

                // RequisicaoId repetido: devolve o pagamento existente, sem nova cobrança.
                ResultadoPagamento.JaProcessado => Ok(resposta.Conteudo),

                ResultadoPagamento.NaoEncontrado => NotFound(resposta.Conteudo),
                ResultadoPagamento.JaAprovado => Conflict(resposta.Conteudo),
                ResultadoPagamento.DadosInvalidos => BadRequest(resposta.Conteudo),
                _ => StatusCode(StatusCodes.Status502BadGateway, resposta.Conteudo)
            };
        }

        /// <summary>Consulta um pagamento pelo Id.</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> ObterPorId(int id)
        {
            var response = await _pagamentoAppService.ObterPorIdAsync(id);
            return response.hasErrors ? NotFound(response) : Ok(response);
        }

        /// <summary>Histórico de tentativas de pagamento de um orçamento.</summary>
        [HttpGet("orcamento/{orcamentoId:int}")]
        public async Task<IActionResult> ObterPorOrcamento(int orcamentoId)
        {
            var response = await _pagamentoAppService.ObterPorOrcamentoIdAsync(orcamentoId);
            return response.hasErrors ? BadRequest(response) : Ok(response);
        }

        /// <summary>Pagamentos de uma Ordem de Serviço.</summary>
        [HttpGet("ordem-servico/{ordemServicoId:int}")]
        public async Task<IActionResult> ObterPorOrdemServico(int ordemServicoId)
        {
            var response = await _pagamentoAppService.ObterPorOrdemServicoIdAsync(ordemServicoId);
            return response.hasErrors ? BadRequest(response) : Ok(response);
        }

        /// <summary>
        /// Consulta o provedor e reaplica o status. É o caminho para reprocessar
        /// notificação perdida e, em desenvolvimento sem URL pública, a única forma de
        /// fechar o ciclo do pagamento.
        /// </summary>
        [HttpPost("{id:int}/sincronizar")]
        public async Task<IActionResult> Sincronizar(int id, CancellationToken cancellationToken)
        {
            var resposta = await _pagamentoAppService.SincronizarAsync(id, cancellationToken);

            return resposta.Resultado switch
            {
                ResultadoPagamento.Ok => Ok(resposta.Conteudo),
                ResultadoPagamento.SemMudanca => NoContent(),
                ResultadoPagamento.NaoEncontrado => NotFound(resposta.Conteudo),
                _ => StatusCode(StatusCodes.Status502BadGateway, resposta.Conteudo)
            };
        }
    }
}
