using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using System.Text.Json;

namespace OrcamentoPagamento.Infrastructure.Messaging
{
    /// <summary>
    /// Publica os eventos do orçamento num tópico SNS. O MessageAttribute "tipoEvento"
    /// permite que cada assinante filtre por tipo (filter policy) em vez de receber tudo.
    /// </summary>
    public class SnsPublicadorEventos : IPublicadorEventos
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly IAmazonSimpleNotificationService _sns;
        private readonly MessagingOptions _options;
        private readonly ILogger<SnsPublicadorEventos> _logger;

        public SnsPublicadorEventos(
            IAmazonSimpleNotificationService sns,
            IOptions<MessagingOptions> options,
            ILogger<SnsPublicadorEventos> logger)
        {
            _sns = sns;
            _options = options.Value;
            _logger = logger;
        }

        public async Task PublicarAsync(string tipoEvento, object payload, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.TopicoEventosArn))
            {
                _logger.LogWarning(
                    "Evento {TipoEvento} nao publicado: Messaging:TopicoEventosArn nao configurado.",
                    tipoEvento);
                return;
            }

            var mensagem = JsonSerializer.Serialize(new
            {
                eventoId = Guid.NewGuid(),
                tipoEvento,
                ocorridoEm = DateTime.UtcNow,
                dados = payload
            }, JsonOptions);

            var request = new PublishRequest
            {
                TopicArn = _options.TopicoEventosArn,
                Message = mensagem,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["tipoEvento"] = new MessageAttributeValue { DataType = "String", StringValue = tipoEvento }
                }
            };

            var resultado = await _sns.PublishAsync(request, cancellationToken);

            _logger.LogInformation(
                "Evento {TipoEvento} publicado no SNS (MessageId {MessageId}).",
                tipoEvento, resultado.MessageId);
        }
    }
}
