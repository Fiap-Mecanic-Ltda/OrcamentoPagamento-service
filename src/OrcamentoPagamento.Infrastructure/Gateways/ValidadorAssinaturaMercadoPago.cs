using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrcamentoPagamento.Domain.Interfaces.Gateways;
using System.Security.Cryptography;
using System.Text;

namespace OrcamentoPagamento.Infrastructure.Gateways
{
    /// <summary>
    /// Valida o header `x-signature` do webhook. É a única coisa que separa uma notificação
    /// legítima de alguém postando `{"data":{"id":"..."}}` na nossa rota pública.
    ///
    /// O header vem no formato `ts=1704908010,v1=&lt;hmac&gt;`. O HMAC-SHA256 é calculado
    /// sobre um manifest montado com o `data.id` da query, o `x-request-id` e o `ts`,
    /// usando o secret do webhook como chave.
    /// </summary>
    public class ValidadorAssinaturaMercadoPago : IValidadorAssinaturaWebhook
    {
        private readonly MercadoPagoOptions _options;
        private readonly ILogger<ValidadorAssinaturaMercadoPago> _logger;

        public ValidadorAssinaturaMercadoPago(
            IOptions<PagamentoOptions> options,
            ILogger<ValidadorAssinaturaMercadoPago> logger)
        {
            _options = options.Value.MercadoPago;
            _logger = logger;
        }

        public bool Validar(string? assinatura, string? requestId, string? dataId)
        {
            if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
            {
                // Sem secret configurado não há como validar. Aceitar é o comportamento
                // utilizável em desenvolvimento, mas precisa ser ruidoso: em produção isso
                // significa rota pública sem autenticação nenhuma.
                _logger.LogWarning(
                    "Pagamentos:MercadoPago:WebhookSecret nao configurado. Notificacao aceita SEM validacao de assinatura.");
                return true;
            }

            if (string.IsNullOrWhiteSpace(assinatura) || string.IsNullOrWhiteSpace(dataId))
            {
                _logger.LogWarning("Notificacao sem x-signature ou sem data.id. Rejeitada.");
                return false;
            }

            var (timestamp, hashRecebido) = Separar(assinatura);

            if (timestamp is null || hashRecebido is null)
            {
                _logger.LogWarning("Header x-signature em formato inesperado: {Assinatura}", assinatura);
                return false;
            }

            // O manifest segue o formato documentado pelo Mercado Pago. O data.id entra em
            // minúsculas; os outros campos são omitidos quando ausentes.
            var manifest = new StringBuilder();
            manifest.Append("id:").Append(dataId.ToLowerInvariant()).Append(';');

            if (!string.IsNullOrWhiteSpace(requestId))
                manifest.Append("request-id:").Append(requestId).Append(';');

            manifest.Append("ts:").Append(timestamp).Append(';');

            var esperado = CalcularHmac(manifest.ToString(), _options.WebhookSecret!);

            // Comparação em tempo fixo: comparar hash com == abre canal de timing.
            var valido = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(esperado),
                Encoding.UTF8.GetBytes(hashRecebido));

            if (!valido)
                _logger.LogWarning(
                    "Assinatura invalida para a notificacao do pagamento {DataId}.", dataId);

            return valido;
        }

        private static (string? Timestamp, string? Hash) Separar(string assinatura)
        {
            string? timestamp = null;
            string? hash = null;

            foreach (var parte in assinatura.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separador = parte.IndexOf('=');
                if (separador <= 0)
                    continue;

                var chave = parte[..separador].Trim();
                var valor = parte[(separador + 1)..].Trim();

                if (chave.Equals("ts", StringComparison.OrdinalIgnoreCase))
                    timestamp = valor;
                else if (chave.Equals("v1", StringComparison.OrdinalIgnoreCase))
                    hash = valor;
            }

            return (timestamp, hash);
        }

        private static string CalcularHmac(string manifest, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
