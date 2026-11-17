using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Infrastructure;

namespace OrcamentoPagamento.API.Extensions
{
    public static class DatabaseExtension
    {
        public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<BancoOrcamentoContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            return services;
        }

        /// <summary>
        /// Aplica as migrations pendentes na subida, como o monólito faz. Banco próprio do
        /// serviço, migrations próprias: nenhum outro serviço depende desse schema.
        /// </summary>
        public static async Task MigrateDatabaseAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<BancoOrcamentoContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                // Nos testes de integracao o provider e o InMemory, que nao tem migrations.
                if (!context.Database.IsRelational())
                {
                    await context.Database.EnsureCreatedAsync();
                    logger.LogInformation("Banco nao relacional: schema criado sem migrations.");
                    return;
                }

                await context.Database.MigrateAsync();
                logger.LogInformation("Migrations do banco de orcamento aplicadas.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao aplicar as migrations do banco de orcamento.");
                throw;
            }
        }
    }
}
