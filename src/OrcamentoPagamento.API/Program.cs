using Microsoft.AspNetCore.HttpOverrides;
using OrcamentoPagamento.API.Extensions;
using OrcamentoPagamento.API.Middlewares;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddControllers();

// Rotas em minúsculas: o roteamento do API Gateway é case-sensitive, então gateway,
// Swagger e Postman precisam falar exatamente a mesma grafia.
builder.Services.AddRouting(options => options.LowercaseUrls = true);

// O tráfego chega pelo API Gateway e pelo Service do Kubernetes. Sem confiar nos headers
// de encaminhamento, o ASP.NET vê o IP e o esquema do proxy, não do cliente.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                               | ForwardedHeaders.XForwardedProto
                               | ForwardedHeaders.XForwardedHost;

    // Proxies gerenciados têm IP dinâmico, por isso as listas ficam vazias.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerWithAuth();

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddDependencyInjection();
builder.Services.AddAutoMapperProfiles();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddPagamentos(builder.Configuration);

var app = builder.Build();

// Banco próprio do serviço: migrations aplicadas na subida, sem seed — o orçamento só
// existe a partir de um evento de itens da Ordem de Serviço.
await app.MigrateDatabaseAsync();

app.UseForwardedHeaders();
app.UseCorrelationId();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Usado pelo healthcheck do Docker Compose e pelas probes do Kubernetes.
app.MapGet("/health", () => Results.Ok(new { status = "ok", servico = "orcamento" })).AllowAnonymous();

app.Run();

// Expõe a classe Program para o WebApplicationFactory dos testes de integração.
public partial class Program { }
