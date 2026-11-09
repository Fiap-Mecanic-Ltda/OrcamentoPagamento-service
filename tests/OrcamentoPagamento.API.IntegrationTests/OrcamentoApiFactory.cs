using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using OrcamentoPagamento.Infrastructure;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace OrcamentoPagamento.API.IntegrationTests
{
    /// <summary>
    /// Sobe a API com banco InMemory e mensageria desligada. A chave JWT é fixada aqui para
    /// que os testes assinem tokens aceitos pela mesma validação usada em produção.
    /// </summary>
    public class OrcamentoApiFactory : WebApplicationFactory<Program>
    {
        public const string ChaveJwt = "chave-de-teste-do-microsservico-de-orcamento-1234567890";
        public const string Issuer = "MechanicLtda.API";
        public const string Audience = "MechanicLtda.Clients";

        private readonly string _nomeBanco = $"orcamento-tests-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("JWT_SECRET_KEY", ChaveJwt);

            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:Issuer"] = Issuer,
                    ["JwtSettings:IssuerCpf"] = "MechanicLtda.Auth.Cpf",
                    ["JwtSettings:Audience"] = Audience,
                    ["Messaging:Habilitado"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                // Troca o SQL Server pelo InMemory. Nao basta remover o DbContextOptions:
                // o AddDbContext do Program deixa um IDbContextOptionsConfiguration que
                // reaplica o UseSqlServer, e o EF recusa dois providers no mesmo container.
                services.RemoveAll<IDbContextOptionsConfiguration<BancoOrcamentoContext>>();
                services.RemoveAll<DbContextOptions<BancoOrcamentoContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<BancoOrcamentoContext>();

                services.AddDbContext<BancoOrcamentoContext>(options =>
                    options.UseInMemoryDatabase(_nomeBanco));
            });
        }

        public string GerarToken(string role = "Administrador")
        {
            var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveJwt));

            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(ClaimTypes.Name, "admin@mechanic.com"),
                    new Claim(ClaimTypes.Role, role)
                },
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public HttpClient CriarClienteAutenticado(string role = "Administrador")
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GerarToken(role));
            return client;
        }
    }
}
