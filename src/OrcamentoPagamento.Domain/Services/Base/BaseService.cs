using OrcamentoPagamento.Domain.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace OrcamentoPagamento.Domain.Services.Base
{
    public abstract class BaseService
    {
        private readonly INotificadorService _notificadorService;

        protected BaseService(INotificadorService notificadorService)
        {
            _notificadorService = notificadorService;
        }

        protected void Notificar(Exception ex, string contexto, ILogger logger)
        {
            logger.LogError(ex, "{Contexto}", contexto);
            _notificadorService.Notificar($"{contexto}: {ex.Message}");
        }

        protected void Notificar(string mensagem) => _notificadorService.Notificar(mensagem);
    }
}
