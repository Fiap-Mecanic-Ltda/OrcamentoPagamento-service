namespace OrcamentoPagamento.API.Middlewares
{
    /// <summary>
    /// Correlaciona a requisição com o registro do API Gateway. O gateway repassa o seu
    /// $context.requestId no header X-Correlation-Id; aqui esse id entra no escopo de log
    /// (aparecendo em todas as linhas da requisição) e volta na resposta. Quando a chamada
    /// não vem do gateway, um id novo é gerado.
    ///
    /// O consumidor da fila usa o mesmo nome de escopo, com o CorrelationId que vem no
    /// evento — um orçamento calculado por mensagem continua rastreável até a requisição
    /// que alterou os itens da OS.
    /// </summary>
    public class CorrelationIdMiddleware
    {
        public const string HeaderName = "X-Correlation-Id";

        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(correlationId))
                correlationId = Guid.NewGuid().ToString("n");

            context.Items[HeaderName] = correlationId;
            context.TraceIdentifier = correlationId;

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = correlationId;
                return Task.CompletedTask;
            });

            using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                await _next(context);
            }
        }
    }

    public static class CorrelationIdMiddlewareExtension
    {
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
            => app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
