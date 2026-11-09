namespace OrcamentoPagamento.Infrastructure.Messaging
{
    /// <summary>
    /// Configuração da mensageria. Vem de appsettings/variáveis de ambiente na seção
    /// "Messaging"; em produção, do SSM Parameter Store via Secret do Kubernetes.
    /// </summary>
    public class MessagingOptions
    {
        public const string SecaoConfiguracao = "Messaging";

        /// <summary>Quando false, nada é publicado nem consumido — só log. Útil em dev e nos testes.</summary>
        public bool Habilitado { get; set; }

        /// <summary>ARN do tópico SNS onde os eventos de orçamento são publicados.</summary>
        public string? TopicoEventosArn { get; set; }

        /// <summary>URL da fila SQS assinada no tópico de itens alterados da Ordem de Serviço.</summary>
        public string? FilaItensAlteradosUrl { get; set; }

        /// <summary>Endpoint alternativo do SNS/SQS (LocalStack no docker-compose).</summary>
        public string? ServiceUrl { get; set; }

        public string Regiao { get; set; } = "us-east-1";

        /// <summary>Mensagens lidas por chamada de ReceiveMessage (1–10).</summary>
        public int MaxMensagensPorLeitura { get; set; } = 10;

        /// <summary>Long polling, em segundos (0–20). Evita leitura em laço curto.</summary>
        public int EsperaLongPollingSegundos { get; set; } = 20;

        /// <summary>Tempo que a mensagem fica invisível enquanto é processada.</summary>
        public int VisibilityTimeoutSegundos { get; set; } = 60;
    }
}
