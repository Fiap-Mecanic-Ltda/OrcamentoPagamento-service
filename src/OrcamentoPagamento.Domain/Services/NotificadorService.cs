using OrcamentoPagamento.Domain.Interfaces.Services;

namespace OrcamentoPagamento.Domain.Services
{
    public class NotificadorService : INotificadorService
    {
        private readonly List<string> _notificacoes = new();

        public void Notificar(string mensagem)
        {
            if (!string.IsNullOrWhiteSpace(mensagem))
                _notificacoes.Add(mensagem);
        }

        public IReadOnlyCollection<string> ObterNotificacoes() => _notificacoes.AsReadOnly();

        public bool TemNotificacao() => _notificacoes.Count > 0;
    }
}
