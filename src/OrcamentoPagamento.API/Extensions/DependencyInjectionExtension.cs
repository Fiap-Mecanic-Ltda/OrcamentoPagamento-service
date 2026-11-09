using OrcamentoPagamento.Application.AppServices;
using OrcamentoPagamento.Application.AppServices.Interfaces;
using OrcamentoPagamento.Application.Mapping;
using OrcamentoPagamento.Domain.Interfaces.Repositories;
using OrcamentoPagamento.Domain.Interfaces.Services;
using OrcamentoPagamento.Domain.Services;
using OrcamentoPagamento.Infrastructure.Repositories;

namespace OrcamentoPagamento.API.Extensions
{
    public static class DependencyInjectionExtension
    {
        public static IServiceCollection AddDependencyInjection(this IServiceCollection services)
        {
            // Notificador — Scoped para que as notificações vivam por requisição
            // (ou por mensagem consumida, que abre o próprio escopo).
            services.AddScoped<INotificadorService, NotificadorService>();

            services.AddScoped<IOrcamentoRepository, OrcamentoRepository>();
            services.AddScoped<IPagamentoRepository, PagamentoRepository>();

            services.AddScoped<IOrcamentoService, OrcamentoService>();
            services.AddScoped<IPagamentoService, PagamentoService>();

            services.AddScoped<IOrcamentoAppService, OrcamentoAppService>();
            services.AddScoped<IPagamentoAppService, PagamentoAppService>();

            // Sem estado e com custo de inicialização (licença/fontes do QuestPDF).
            services.AddSingleton<IOrcamentoPdfAppService, OrcamentoPdfAppService>();

            return services;
        }

        public static IServiceCollection AddAutoMapperProfiles(this IServiceCollection services)
        {
            services.AddAutoMapper(cfg => cfg.AddProfile<OrcamentoProfile>());
            return services;
        }
    }
}
