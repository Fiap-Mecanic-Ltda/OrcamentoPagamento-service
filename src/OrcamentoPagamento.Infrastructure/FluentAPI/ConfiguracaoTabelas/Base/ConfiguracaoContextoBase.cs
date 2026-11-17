using Microsoft.EntityFrameworkCore;

namespace OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas.Base
{
    public abstract class ConfiguracaoContextoBase
    {
        public abstract void Configurar(ModelBuilder modelBuilder);
    }
}
