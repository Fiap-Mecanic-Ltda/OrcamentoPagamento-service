using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OrcamentoPagamento.Application.Security;
using System.Text;

namespace OrcamentoPagamento.API.Extensions
{
    public static class AuthenticationExtension
    {
        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer();

            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var jwtSettings = configuration.GetSection("JwtSettings");

                var secretKey = Encoding.UTF8.GetBytes(JwtSecretProvider.GetSecretKey(configuration));

                // O microsservico nao emite token: so valida os emitidos pela API (login
                // e-mail/senha) e pela Lambda de autenticacao por CPF. Mesma chave
                // simetrica, mesmos emissores aceitos.
                var issuers = new[] { jwtSettings["Issuer"], jwtSettings["IssuerCpf"] }
                    .Where(issuer => !string.IsNullOrWhiteSpace(issuer))
                    .ToArray();

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuers = issuers,
                    ValidAudience = jwtSettings["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(secretKey),
                    ClockSkew = TimeSpan.Zero
                };
            });

            return services;
        }
    }
}
