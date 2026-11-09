using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrcamentoPagamento.Domain.Enums;
using OrcamentoPagamento.Domain.Exceptions;
using OrcamentoPagamento.Domain.Interfaces.Gateways;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrcamentoPagamento.Infrastructure.Gateways
{
    /// <summary>
    /// Adapter do Mercado Pago (Checkout API). É o **único** arquivo do serviço que conhece
    /// o vocabulário deles: nomes de campo, strings de status e formato de data.
    ///
    /// Usa HttpClient direto em vez do SDK: o adapter é pequeno, fica trivial de fakear, e
    /// não herda surpresa de versão de pacote.
    /// </summary>
    public class MercadoPagoGateway : IGatewayPagamento
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;
        private readonly MercadoPagoOptions _options;
        private readonly ILogger<MercadoPagoGateway> _logger;

        public MercadoPagoGateway(
            HttpClient http,
            IOptions<PagamentoOptions> options,
            ILogger<MercadoPagoGateway> logger)
        {
            _http = http;
            _options = options.Value.MercadoPago;
            _logger = logger;
        }

        public string Nome => Domain.Entities.Pagamento.ProvedorMercadoPago;

        public async Task<RespostaGatewayPagamento> CriarAsync(RequisicaoGatewayPagamento requisicao, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.AccessToken))
                throw new GatewayPagamentoException(
                    "Pagamentos:MercadoPago:AccessToken nao configurado.");

            var corpo = new Dictionary<string, object?>
            {
                ["transaction_amount"] = requisicao.Valor,
                ["token"] = requisicao.Token,
                ["description"] = requisicao.Descricao,
                ["installments"] = requisicao.Parcelas,
                ["payment_method_id"] = requisicao.MetodoPagamento,
                ["external_reference"] = requisicao.ReferenciaExterna,
                ["payer"] = MontarPagador(requisicao)
            };

            if (requisicao.EmissorId.HasValue)
                corpo["issuer_id"] = requisicao.EmissorId.Value;

            if (!string.IsNullOrWhiteSpace(_options.NotificationUrl))
                corpo["notification_url"] = _options.NotificationUrl;

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/payments")
            {
                Content = JsonContent.Create(corpo)
            };

            // Idempotência do lado do provedor: um retry nosso com a mesma chave nao gera
            // segunda cobranca.
            request.Headers.Add("X-Idempotency-Key", requisicao.ChaveIdempotencia.ToString());
            Autenticar(request);

            return await EnviarAsync(request, "criar pagamento", cancellationToken);
        }

        public async Task<RespostaGatewayPagamento> ObterAsync(string idExterno, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.AccessToken))
                throw new GatewayPagamentoException(
                    "Pagamentos:MercadoPago:AccessToken nao configurado.");

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/payments/{idExterno}");
            Autenticar(request);

            return await EnviarAsync(request, $"consultar pagamento {idExterno}", cancellationToken);
        }

        private void Autenticar(HttpRequestMessage request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private static Dictionary<string, object?> MontarPagador(RequisicaoGatewayPagamento requisicao)
        {
            var pagador = new Dictionary<string, object?>
            {
                ["email"] = requisicao.PagadorEmail
            };

            // O documento é o que força o resultado nos testes (CPF 12345678909 junto do
            // nome APRO/OTHE no cartão), então vai sempre que vier preenchido.
            if (!string.IsNullOrWhiteSpace(requisicao.PagadorDocumento))
            {
                pagador["identification"] = new Dictionary<string, object?>
                {
                    ["type"] = string.IsNullOrWhiteSpace(requisicao.PagadorTipoDocumento)
                        ? "CPF"
                        : requisicao.PagadorTipoDocumento,
                    ["number"] = requisicao.PagadorDocumento
                };
            }

            return pagador;
        }

        private async Task<RespostaGatewayPagamento> EnviarAsync(
            HttpRequestMessage request,
            string operacao,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage resposta;

            try
            {
                resposta = await _http.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new GatewayPagamentoException(
                    $"Falha de comunicacao com o Mercado Pago ao {operacao}.", ex);
            }

            var conteudo = await resposta.Content.ReadAsStringAsync(cancellationToken);

            if (!resposta.IsSuccessStatusCode)
            {
                // 4xx aqui é erro de integração (token expirado, campo faltando, credencial
                // errada) — não é recusa de pagamento. Recusa vem com 2xx e status rejected.
                _logger.LogError(
                    "Mercado Pago respondeu {StatusCode} ao {Operacao}: {Corpo}",
                    (int)resposta.StatusCode, operacao, conteudo);

                throw new GatewayPagamentoException(
                    $"Mercado Pago respondeu {(int)resposta.StatusCode} ao {operacao}.");
            }

            return Interpretar(conteudo, operacao);
        }

        private RespostaGatewayPagamento Interpretar(string conteudo, string operacao)
        {
            try
            {
                using var documento = JsonDocument.Parse(conteudo);
                var raiz = documento.RootElement;

                var statusCru = LerTexto(raiz, "status");

                return new RespostaGatewayPagamento
                {
                    IdExterno = LerTexto(raiz, "id"),
                    Status = MapearStatus(statusCru),
                    StatusDetalhe = LerTexto(raiz, "status_detail"),
                    MetodoPagamento = LerTexto(raiz, "payment_method_id"),
                    Parcelas = raiz.TryGetProperty("installments", out var parcelas) && parcelas.TryGetInt32(out var valor)
                        ? valor
                        : null,
                    ModoProducao = raiz.TryGetProperty("live_mode", out var liveMode)
                                   && liveMode.ValueKind == JsonValueKind.True,
                    UltimaAtualizacao = LerData(raiz, "date_last_updated")
                                        ?? LerData(raiz, "date_created")
                };
            }
            catch (JsonException ex)
            {
                throw new GatewayPagamentoException(
                    $"Resposta do Mercado Pago ao {operacao} nao pode ser interpretada.", ex);
            }
        }

        /// <summary>
        /// O vocabulário do Mercado Pago para o nosso. Fica no adapter de propósito: um
        /// segundo provedor terá strings diferentes, e o domínio não deve saber disso.
        /// </summary>
        private StatusPagamento MapearStatus(string? statusCru)
        {
            return statusCru?.ToLowerInvariant() switch
            {
                "pending" => StatusPagamento.Pendente,
                "in_process" => StatusPagamento.EmProcessamento,
                "authorized" => StatusPagamento.Autorizado,
                "approved" => StatusPagamento.Aprovado,
                "rejected" => StatusPagamento.Recusado,
                "cancelled" or "canceled" => StatusPagamento.Cancelado,
                "refunded" => StatusPagamento.Devolvido,
                "in_mediation" => StatusPagamento.EmDisputa,
                "charged_back" => StatusPagamento.Estornado,

                // Status novo do provedor é tratado como pendente, e não como erro: deixa a
                // cobrança em aberto e aparece no log para ser mapeado depois.
                _ => TratarStatusDesconhecido(statusCru)
            };
        }

        private StatusPagamento TratarStatusDesconhecido(string? statusCru)
        {
            _logger.LogWarning(
                "Status '{Status}' nao reconhecido do Mercado Pago. Tratado como Pendente.",
                statusCru ?? "(nulo)");

            return StatusPagamento.Pendente;
        }

        private static string? LerTexto(JsonElement raiz, string propriedade)
        {
            if (!raiz.TryGetProperty(propriedade, out var valor))
                return null;

            return valor.ValueKind switch
            {
                JsonValueKind.String => valor.GetString(),
                JsonValueKind.Number => valor.ToString(),
                _ => null
            };
        }

        private static DateTime? LerData(JsonElement raiz, string propriedade)
        {
            var texto = LerTexto(raiz, propriedade);

            return DateTime.TryParse(
                texto,
                null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var data)
                ? data
                : null;
        }
    }
}
