using Microsoft.OpenApi.Models;

namespace OrcamentoPagamento.API.Extensions
{
    public static class SwaggerExtension
    {
        public static IServiceCollection AddSwaggerWithAuth(this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "MechanicLtda - Orcamento e Pagamento",
                    Version = "v1",
                    Description = "Microsservico de orcamentos da oficina. Consome os eventos de "
                                + "itens da Ordem de Servico, calcula o orcamento no proprio banco "
                                + "e expoe consulta, ajuste manual e exportacao em PDF."
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Informe: Bearer {seu_token}"
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });

                var xmlPath = Path.Combine(AppContext.BaseDirectory, "OrcamentoPagamento.API.xml");
                if (File.Exists(xmlPath))
                    options.IncludeXmlComments(xmlPath);
            });

            return services;
        }
    }
}
