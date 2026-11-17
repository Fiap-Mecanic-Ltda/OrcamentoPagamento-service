using Microsoft.Extensions.Configuration;

namespace OrcamentoPagamento.Application.Security
{
    /// <summary>
    /// Mesma resolução de chave do monólito: os tokens emitidos pela Lambda de
    /// autenticação e pelo login da API precisam validar aqui com a mesma chave simétrica.
    /// </summary>
    public static class JwtSecretProvider
    {
        public static string GetSecretKey(IConfiguration configuration)
        {
            return Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
                   ?? configuration["JWT_SECRET_KEY"]
                   ?? configuration["JwtSettings:SecretKey"]
                   ?? throw new InvalidOperationException(
                       "A chave JWT nao esta configurada. Defina 'JWT_SECRET_KEY' como variavel de ambiente ou em configuracao.");
        }
    }
}
