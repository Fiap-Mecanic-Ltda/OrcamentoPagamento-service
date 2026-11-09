using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using OrcamentoPagamento.Application.AppServices.Interfaces;
using OrcamentoPagamento.Application.Eventos;
using OrcamentoPagamento.Infrastructure.Messaging;
using System.Text.Json;

namespace OrcamentoPagamento.API.Messaging
{
    /// <summary>
    /// Consome a fila SQS assinada no tópico de itens alterados da Ordem de Serviço e
    /// manda recalcular o orçamento.
    ///
    /// Fica no host (e não na Infrastructure) porque precisa da camada de Application —
    /// Infrastructure depende só do Domain, como no monólito.
    ///
    /// Política de erro: a mensagem só é apagada quando o processamento dá certo. Erro
    /// deixa a mensagem voltar à fila depois do visibility timeout e, esgotado o
    /// maxReceiveCount da redrive policy, ela cai na DLQ. Mensagem com JSON inválido é
    /// apagada na hora: reprocessar não vai consertar o payload.
    /// </summary>
    public class SqsConsumidorItensAlterados : BackgroundService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IAmazonSQS _sqs;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MessagingOptions _options;
        private readonly ILogger<SqsConsumidorItensAlterados> _logger;

        public SqsConsumidorItensAlterados(
            IAmazonSQS sqs,
            IServiceScopeFactory scopeFactory,
            IOptions<MessagingOptions> options,
            ILogger<SqsConsumidorItensAlterados> logger)
        {
            _sqs = sqs;
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(_options.FilaItensAlteradosUrl))
            {
                _logger.LogWarning(
                    "Consumidor nao iniciado: Messaging:FilaItensAlteradosUrl nao configurada.");
                return;
            }

            _logger.LogInformation(
                "Consumidor da fila {Fila} iniciado (long polling de {Espera}s).",
                _options.FilaItensAlteradosUrl, _options.EsperaLongPollingSegundos);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var resposta = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                    {
                        QueueUrl = _options.FilaItensAlteradosUrl,
                        MaxNumberOfMessages = _options.MaxMensagensPorLeitura,
                        WaitTimeSeconds = _options.EsperaLongPollingSegundos,
                        VisibilityTimeout = _options.VisibilityTimeoutSegundos
                    }, stoppingToken);

                    foreach (var mensagem in resposta.Messages ?? new List<Message>())
                        await ProcessarMensagemAsync(mensagem, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Falha na própria leitura (rede, credencial, fila inexistente): espera
                    // antes de tentar de novo para não girar em laço contra o SQS.
                    _logger.LogError(ex, "Falha ao ler a fila {Fila}.", _options.FilaItensAlteradosUrl);
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            _logger.LogInformation("Consumidor da fila encerrado.");
        }

        private async Task ProcessarMensagemAsync(Message mensagem, CancellationToken cancellationToken)
        {
            OrdemServicoItensAlteradosEvent? evento;

            try
            {
                evento = Desserializar(mensagem.Body);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "Mensagem {MessageId} descartada: corpo nao e um JSON valido do evento.",
                    mensagem.MessageId);
                await ApagarAsync(mensagem, cancellationToken);
                return;
            }

            if (evento is null || evento.OrdemServicoId <= 0)
            {
                _logger.LogError(
                    "Mensagem {MessageId} descartada: evento sem OrdemServicoId.",
                    mensagem.MessageId);
                await ApagarAsync(mensagem, cancellationToken);
                return;
            }

            var correlationId = string.IsNullOrWhiteSpace(evento.CorrelationId)
                ? Guid.NewGuid().ToString("n")
                : evento.CorrelationId;

            using var escopoLog = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId
            });

            // Escopo de DI próprio por mensagem: o DbContext e o notificador são Scoped e
            // não podem ser compartilhados entre mensagens.
            using var scope = _scopeFactory.CreateScope();
            var appService = scope.ServiceProvider.GetRequiredService<IOrcamentoAppService>();

            var resultado = await appService.RecalcularAsync(evento, cancellationToken);

            if (resultado.hasErrors)
            {
                // Não apaga: a mensagem volta para a fila e, no limite, para a DLQ.
                _logger.LogError(
                    "Falha ao recalcular o orcamento da OS {OrdemServicoId} (mensagem {MessageId}): {Erros}",
                    evento.OrdemServicoId, mensagem.MessageId, string.Join(" | ", resultado.getErrors));
                return;
            }

            if (resultado.getResponse is null)
                _logger.LogInformation(
                    "Evento {EventoId} da OS {OrdemServicoId} ignorado por idempotencia.",
                    evento.EventoId, evento.OrdemServicoId);
            else
                _logger.LogInformation(
                    "Orcamento {OrcamentoId} da OS {OrdemServicoId} recalculado: total {Total}.",
                    resultado.getResponse.Id, evento.OrdemServicoId, resultado.getResponse.ValorTotalGeral);

            await ApagarAsync(mensagem, cancellationToken);
        }

        /// <summary>
        /// Aceita tanto o evento direto (fila escrita pelo produtor) quanto o envelope do
        /// SNS, em que o evento vem como string no campo "Message" — é o formato que chega
        /// numa fila assinada num tópico sem raw message delivery.
        /// </summary>
        private static OrdemServicoItensAlteradosEvent? Desserializar(string corpo)
        {
            using var documento = JsonDocument.Parse(corpo);

            if (documento.RootElement.ValueKind == JsonValueKind.Object
                && documento.RootElement.TryGetProperty("Message", out var envelope)
                && envelope.ValueKind == JsonValueKind.String)
            {
                var interno = envelope.GetString();
                return string.IsNullOrWhiteSpace(interno)
                    ? null
                    : JsonSerializer.Deserialize<OrdemServicoItensAlteradosEvent>(interno, JsonOptions);
            }

            return JsonSerializer.Deserialize<OrdemServicoItensAlteradosEvent>(corpo, JsonOptions);
        }

        private async Task ApagarAsync(Message mensagem, CancellationToken cancellationToken)
        {
            try
            {
                await _sqs.DeleteMessageAsync(
                    _options.FilaItensAlteradosUrl,
                    mensagem.ReceiptHandle,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // A mensagem vai reaparecer na fila; a idempotência do cálculo cobre isso.
                _logger.LogError(ex,
                    "Falha ao apagar a mensagem {MessageId} da fila.", mensagem.MessageId);
            }
        }
    }
}
