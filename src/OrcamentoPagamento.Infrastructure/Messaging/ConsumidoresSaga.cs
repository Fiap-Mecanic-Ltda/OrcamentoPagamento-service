using MassTransit;
using MechanicLtda.Contratos;
using Microsoft.Extensions.Logging;
using OrcamentoPagamento.Domain.Interfaces.Services;
using OrcamentoPagamento.Domain.Messaging;

namespace OrcamentoPagamento.Infrastructure.Messaging
{
    /// <summary>
    /// Filas do Billing: billing-&lt;comando-em-kebab&gt;, cada uma ligada ao exchange fanout do
    /// comando (ex.: billing-gerar-orcamento ← MechanicLtda.Contratos:GerarOrcamento), como
    /// define docs/contratos-eventos.md.
    /// </summary>
    public static class FilasBilling
    {
        public const string GerarOrcamento    = "billing-gerar-orcamento";
        public const string CancelarOrcamento = "billing-cancelar-orcamento";
        public const string EstornarPagamento = "billing-estornar-pagamento";

        public static readonly IReadOnlySet<string> Todas = new HashSet<string> { GerarOrcamento, CancelarOrcamento, EstornarPagamento };
    }

    /// <summary>
    /// Base dos consumidores: leva o x-correlation-id da mensagem para os logs e para as
    /// respostas publicadas. Cada mensagem tem o próprio escopo de DI (DbContext, notificador).
    /// </summary>
    public abstract class ConsumidorSaga<T> : IConsumer<T> where T : MensagemSaga
    {
        private readonly ILogger _logger;

        protected ConsumidorSaga(ILogger logger) => _logger = logger;

        public async Task Consume(ConsumeContext<T> context)
        {
            var requisicao = context.Headers.Get<string>(ContextoCorrelacao.CabecalhoRequisicao);
            ContextoCorrelacao.CorrelationIdRequisicao = requisicao;

            using (_logger.BeginScope(new Dictionary<string, object?>
                   {
                       ["CorrelationId"]  = requisicao,
                       ["SagaId"]         = context.Message.CorrelationId,
                       ["OrdemServicoId"] = context.Message.OrdemServicoId,
                   }))
            {
                _logger.LogInformation("{Mensagem} recebido para a OS {OrdemServicoId}.", typeof(T).Name, context.Message.OrdemServicoId);
                await Tratar(context.Message, context.CancellationToken);
            }
        }

        protected abstract Task Tratar(T mensagem, CancellationToken ct);
    }

    public class GerarOrcamentoConsumer : ConsumidorSaga<GerarOrcamento>
    {
        private readonly ISagaOrcamentoService _saga;

        public GerarOrcamentoConsumer(ISagaOrcamentoService saga, ILogger<GerarOrcamentoConsumer> logger) : base(logger) => _saga = saga;

        protected override Task Tratar(GerarOrcamento mensagem, CancellationToken ct) => _saga.TratarAsync(mensagem, ct);
    }

    public class CancelarOrcamentoConsumer : ConsumidorSaga<CancelarOrcamento>
    {
        private readonly ISagaOrcamentoService _saga;

        public CancelarOrcamentoConsumer(ISagaOrcamentoService saga, ILogger<CancelarOrcamentoConsumer> logger) : base(logger) => _saga = saga;

        protected override Task Tratar(CancelarOrcamento mensagem, CancellationToken ct) => _saga.TratarAsync(mensagem, ct);
    }

    public class EstornarPagamentoConsumer : ConsumidorSaga<EstornarPagamento>
    {
        private readonly ISagaOrcamentoService _saga;

        public EstornarPagamentoConsumer(ISagaOrcamentoService saga, ILogger<EstornarPagamentoConsumer> logger) : base(logger) => _saga = saga;

        protected override Task Tratar(EstornarPagamento mensagem, CancellationToken ct) => _saga.TratarAsync(mensagem, ct);
    }
}
