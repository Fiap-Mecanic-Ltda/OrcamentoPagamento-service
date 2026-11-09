namespace OrcamentoPagamento.Domain.Interfaces.Services
{
    /// <summary>
    /// Coleta as falhas ocorridas no escopo da requisição (ou da mensagem consumida).
    ///
    /// Mesmo papel do notificador do monólito, com contrato simplificado: lá a falha é a
    /// entidade de domínio `Notificacao`, que alimenta o painel de integrações. Aqui é só
    /// uma string — este serviço não tem esse painel nem a tabela, e o que precisa sair
    /// daqui é log estruturado com o CorrelationId.
    /// </summary>
    public interface INotificadorService
    {
        void Notificar(string mensagem);
        IReadOnlyCollection<string> ObterNotificacoes();
        bool TemNotificacao();
    }
}
