# ADR-004 — Participar da Saga da OS pelo RabbitMQ

- **Status:** aceito (substitui a [ADR-002](ADR-002-contrato-de-eventos.md))
- **Data:** 2026-11-17

## Contexto

Na Fase 4 a Ordem de Serviço saiu do monólito para o `ordemservico-service`, que orquestra
uma Saga (ADR-001 e ADR-002 daquele repositório) com o Billing (este serviço) e a Execução.
O contrato é [contratos-eventos.md](../contratos-eventos.md), cópia idêntica nos três
repositórios: RabbitMQ, um exchange `fanout` por mensagem (`MechanicLtda.Contratos:<Nome>`),
JSON puro em `camelCase`, cabeçalhos `correlation-id` e `x-correlation-id`.

Este serviço ainda consumia o evento próprio `ordem-servico.itens-alterados` de uma fila SQS
e publicava `orcamento.*`/`pagamento.*` num tópico SNS. O monólito deixou de produzir o
evento e o OS Service não fala SNS, então a Saga ficava parada em `AguardandoOrcamento`.

## Decisão

1. **Mensageria: RabbitMQ com MassTransit 8**, a mesma pilha do OS Service e da Execução,
   com serialização JSON pura nos dois sentidos. SNS, SQS, a DLQ e o LocalStack saem do
   serviço e do Terraform.
2. **Filas do Billing**, uma por comando, ligadas ao exchange do comando:

   | Fila | Comando | Resposta |
   |---|---|---|
   | `billing-gerar-orcamento` | `GerarOrcamento` | `OrcamentoGerado` ou `OrcamentoFalhou` |
   | `billing-cancelar-orcamento` | `CancelarOrcamento` | `OrcamentoCancelado` |
   | `billing-estornar-pagamento` | `EstornarPagamento` | `PagamentoEstornado` |

3. **Eventos que nascem aqui**, fora de um comando:

   | Origem | Evento |
   |---|---|
   | Pagamento aprovado (`POST /api/pagamento`, webhook ou sincronização) | `PagamentoConfirmado`: vale como a aprovação, a OS vai para Execução |
   | Pagamento recusado | `PagamentoFalhou`: a OS continua em Aguardando Aprovação |
   | `PATCH /api/orcamento/{id}/aprovar` | `OrcamentoAprovado` (opcional, só informativo) |
   | `PATCH /api/orcamento/{id}/recusar` | `OrcamentoRecusado`: a OS é cancelada |

4. **Status do orçamento** (`Aberto`, `Aprovado`, `Pago`, `Recusado`, `Cancelado`) e o
   `CorrelationId` da Saga que o gerou ficam na tabela `Orcamentos`. Só orçamento `Aberto`
   ou `Aprovado` aceita pagamento (equivale à OS em Aguardando Aprovação); os outros
   respondem `409`.
5. **Idempotência pelo par `correlationId` + nome da mensagem**, como pede o contrato,
   guardada no próprio estado: o orçamento grava o `correlationId` que o gerou, e o
   cancelamento e o estorno gravam o status final. Uma reentrega encontra esse estado e
   republica a mesma resposta (o OS Service ignora repetidas). Um `GerarOrcamento` de uma
   Saga nova (outro `correlationId`) recalcula e reabre o orçamento da OS. O estorno usa
   uma chave de idempotência estável no Mercado Pago (`EstornarPagamento#<saga>#<pagamento>`),
   então uma reentrega não devolve o dinheiro duas vezes.
6. **Erros**: item com `tipo` desconhecido, quantidade inválida, lista vazia ou total zero
   viram `OrcamentoFalhou` (o OS Service pede novo diagnóstico). Erro técnico (banco,
   Mercado Pago) é reprocessado pelo MassTransit (retry exponencial, 5 tentativas) e, esgotado,
   vai para a fila `_error`. As respostas publicadas num consumidor só saem se ele terminar
   sem erro (outbox em memória).
7. **Estorno**: devolução total no Mercado Pago (`POST /v1/payments/{id}/refunds`) e nova
   consulta do pagamento; `refunded` vira `Devolvido`. Pagamento ainda em aberto é cancelado
   em vez de devolvido. Pagamento já recusado, cancelado ou devolvido só confirma.
8. **Eventos internos** `orcamento.*` e `pagamento.*` continuam existindo, mas só no log
   estruturado (auditoria e New Relic): nenhum serviço os consome.
9. **Rota manual** `POST /api/orcamento/recalcular` continua, para operação e testes. Ela não
   participa da Saga (não grava `CorrelationId` nem muda status) e mantém a idempotência
   própria por `eventoId`.

## Consequências

- Veículo, telefone e descrição do problema chegam como campos **opcionais** do
  `GerarOrcamento` (`veiculo`, `telefoneCliente`, `descricaoProblema`) e vão para o
  `OrdemServicoSnapshot` que alimenta o PDF. O que vier nulo aparece como "não informado".
- `itens[].tipo` chega como texto (`Peca`, `Insumo`, `Servico`) e é convertido para
  `CategoriaItem`. A rota manual continua recebendo a categoria numérica.
- O contrato diz que o Billing envia o e-mail de aprovação ao cliente. Este serviço ainda não
  tem SMTP: o orçamento gerado fica no log com o e-mail do cliente e o cliente o vê pelo Web.
- Os eventos publicados numa requisição HTTP (pagamento, aprovação, recusa) vão direto ao
  broker depois de gravar no banco, sem outbox transacional. Se o RabbitMQ estiver fora no
  momento de um pagamento aprovado, o pagamento fica gravado e a falha fica no log com o
  pagamento e a OS. A recusa e a aprovação devolvem erro e podem ser repetidas. Um outbox no
  SQL Server (MassTransit EF outbox) é o próximo passo se isso virar problema.
- Com `Mensageria:Habilitada=false` (padrão fora do compose e do cluster) o serviço sobe sem
  broker e as mensagens só vão para o log, como antes com o SNS desligado.
- No OS Service, o simulador do Billing precisa ficar desligado (`SIMULADOR_BILLING=false`)
  quando este serviço estiver no mesmo RabbitMQ, senão os dois respondem aos mesmos comandos.
