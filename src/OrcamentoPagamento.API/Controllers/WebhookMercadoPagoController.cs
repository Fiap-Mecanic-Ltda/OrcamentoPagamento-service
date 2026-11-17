using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrcamentoPagamento.API.ViewModels;
using OrcamentoPagamento.Application.AppServices.Interfaces;
using OrcamentoPagamento.Domain.Interfaces.Gateways;

namespace OrcamentoPagamento.API.Controllers
{
    /// <summary>
    /// Rota pública que recebe as notificações do Mercado Pago.
    ///
    /// É a única rota sem JWT neste serviço — o provedor não tem como mandar o nosso token.
    /// Em troca, a autenticidade vem da validação de `x-signature`, e o corpo da
    /// notificação nunca é fonte de verdade: ela só diz *qual* pagamento mudou, e o estado
    /// real é buscado no provedor.
    ///
    /// Prazos que o provedor impõe e que ditam o desenho: confirmação em 22 segundos, e
    /// reenvio a cada 15 minutos sem ela. Por isso a resposta é 200 em quase todo caso —
    /// 4xx/5xx aqui só gera reenvio eterno de uma mensagem que não vai melhorar.
    /// </summary>
    [ApiController]
    [Route("api/webhooks/mercadopago")]
    [AllowAnonymous]
    public class WebhookMercadoPagoController : ControllerBase
    {
        private const string TipoPagamento = "payment";

        private readonly IPagamentoAppService _pagamentoAppService;
        private readonly IValidadorAssinaturaWebhook _validador;
        private readonly ILogger<WebhookMercadoPagoController> _logger;

        public WebhookMercadoPagoController(
            IPagamentoAppService pagamentoAppService,
            IValidadorAssinaturaWebhook validador,
            ILogger<WebhookMercadoPagoController> logger)
        {
            _pagamentoAppService = pagamentoAppService;
            _validador = validador;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Receber(
            [FromBody] NotificacaoMercadoPagoViewModel notificacao,
            CancellationToken cancellationToken)
        {
            // O data.id pode vir no corpo ou na query, dependendo de como a notificação foi
            // configurada no painel. O da query tem precedência porque é o que entra no
            // manifest da assinatura.
            var dataIdQuery = Request.Query["data.id"].FirstOrDefault();
            var dataId = string.IsNullOrWhiteSpace(dataIdQuery) ? notificacao?.Data?.Id : dataIdQuery;

            var assinatura = Request.Headers["x-signature"].FirstOrDefault();
            var requestId = Request.Headers["x-request-id"].FirstOrDefault();

            if (!_validador.Validar(assinatura, requestId, dataId))
                return Unauthorized();

            var tipo = notificacao?.Type ?? notificacao?.Topic;

            if (!string.Equals(tipo, TipoPagamento, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Notificacao de tipo '{Tipo}' ignorada: este servico so trata '{Esperado}'.",
                    tipo ?? "(nulo)", TipoPagamento);
                return Ok();
            }

            if (string.IsNullOrWhiteSpace(dataId))
            {
                _logger.LogWarning("Notificacao sem data.id. Nada a fazer.");
                return Ok();
            }

            using var escopoLog = _logger.BeginScope(new Dictionary<string, object>
            {
                ["PagamentoExterno"] = dataId,
                ["CorrelationId"] = requestId ?? HttpContext.TraceIdentifier
            });

            var resposta = await _pagamentoAppService.SincronizarPorIdExternoAsync(dataId, cancellationToken);

            // Falha de comunicação com o provedor é o único caso em que vale deixar o
            // provedor reenviar: a mensagem está boa, nós é que não conseguimos confirmar.
            if (resposta.Resultado == ResultadoPagamento.FalhaProvedor)
            {
                _logger.LogError(
                    "Falha ao confirmar o pagamento {DataId} no provedor: {Erros}. Deixando reenviar.",
                    dataId, string.Join(" | ", resposta.Conteudo.getErrors));

                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            // Pagamento desconhecido, notificação repetida ou atrasada: tudo 200. A
            // mensagem não vai ficar melhor numa próxima tentativa.
            return Ok();
        }
    }
}
