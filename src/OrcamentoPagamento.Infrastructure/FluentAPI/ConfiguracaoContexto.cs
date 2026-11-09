using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas;
using OrcamentoPagamento.Infrastructure.FluentAPI.ConfiguracaoTabelas.Base;

namespace OrcamentoPagamento.Infrastructure.FluentAPI
{
    public static class ConfiguracaoContexto
    {
        private static readonly ConfiguracaoContextoBase[] Configuracoes =
        {
            new OrcamentoConfig(),
            new ItemOrcamentoConfig(),
            new OrdemServicoSnapshotConfig(),
            new PagamentoConfig()
        };

        public static void AplicarConfiguracoes(ModelBuilder modelBuilder)
        {
            foreach (var configuracao in Configuracoes)
                configuracao.Configurar(modelBuilder);
        }
    }
}
