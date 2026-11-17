using MechanicLtda.Contratos;
using OrcamentoPagamento.Domain.Entities;

namespace OrcamentoPagamento.Domain.Messaging
{
    /// <summary>Monta as respostas da Saga com os campos comuns do contrato.</summary>
    public static class MensagensSaga
    {
        /// <summary>Responde a uma mensagem recebida: mesmo correlationId e ordemServicoId.</summary>
        public static T Responder<T>(MensagemSaga origem, T resposta) where T : MensagemSaga =>
            resposta with { CorrelationId = origem.CorrelationId, OrdemServicoId = origem.OrdemServicoId, DataHora = DateTime.UtcNow };

        /// <summary>Mensagem originada aqui (pagamento, aprovação): usa a Saga gravada no orçamento.</summary>
        public static T DoOrcamento<T>(Orcamento orcamento, T mensagem) where T : MensagemSaga =>
            mensagem with { CorrelationId = orcamento.CorrelationId ?? Guid.Empty, OrdemServicoId = orcamento.OrdemServicoId, DataHora = DateTime.UtcNow };
    }
}
