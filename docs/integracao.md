# Integração — como este serviço conversa com os outros

Desde a [ADR-004](adr/ADR-004-saga-rabbitmq.md), o orçamento é pedido pela **Saga da OS**,
orquestrada pelo [ordemservico-service](https://github.com/Fiap-Mecanic-Ltda/ordemservico-service),
pelo RabbitMQ. O contrato completo está em [contratos-eventos.md](contratos-eventos.md) (cópia
idêntica nos três repositórios: qualquer mudança passa por PR nos três).

## Fluxo feliz

```text
OS Service                    RabbitMQ                     Billing (este serviço)          Web
    │ DiagnosticoConcluido        │                                 │                         │
    │── GerarOrcamento ──────────▶│── billing-gerar-orcamento ────▶│ calcula e grava         │
    │◀─ OrcamentoGerado ──────────│◀────────────────────────────────│                         │
    │ OS: AguardandoAprovacao     │                                 │◀── POST /api/pagamento ─│
    │◀─ PagamentoConfirmado ──────│◀────────────────────────────────│ Mercado Pago aprovou    │
    │ OS: EmExecucao              │                                 │                         │
    │── IniciarExecucao ─────────▶│ (Execução)                      │                         │
```

Recusa pelo cliente: `PATCH /api/orcamento/{id}/recusar` publica `OrcamentoRecusado` e a OS
vai para `Cancelada`. Pagamento recusado publica `PagamentoFalhou` e a OS continua esperando
nova tentativa. Compensação (prazo vencido ou execução falhou): o OS Service envia
`CancelarOrcamento` e, se houve pagamento, `EstornarPagamento`; este serviço cancela,
devolve no Mercado Pago e confirma com `OrcamentoCancelado` e `PagamentoEstornado`.

## Para validar localmente

1. OS Service com o compose dele (SQL Server, RabbitMQ na 5672, API na 8081), com
   `SIMULADOR_BILLING=false` no `.env` de lá.
2. Banco deste serviço pelo OrcamentoPagamento-InfraSGBD.
3. Este serviço: `docker compose up --build` (API na 8083), apontando para o RabbitMQ do OS
   Service (`RABBITMQ_HOST=host.docker.internal`, usuário e senha do `.env` de lá).
4. No painel do RabbitMQ (`http://localhost:15672`) devem aparecer as filas
   `billing-gerar-orcamento`, `billing-cancelar-orcamento` e `billing-estornar-pagamento`
   ligadas aos exchanges `MechanicLtda.Contratos:*`.
5. Abrir uma OS e levá-la ao diagnóstico no OS Service. Com a Execução simulada, o orçamento
   aparece em `GET /api/orcamento/ordem-servico/{id}` e a OS vai para `AguardandoAprovacao`.
   `POST /api/pagamento` com token `APRO` leva a OS para `EmExecucao`.

## Web (MechanicLtda.Web)

O Web chama este serviço por HTTP (via API Gateway, [ADR-003](adr/ADR-003-consumo-pelo-web-via-api-gateway.md)):

| Ação | Rota |
|---|---|
| Ver o orçamento da OS | `GET /api/orcamento/ordem-servico/{osId}` (campo `status`/`statusDescricao`) |
| Aprovar pagando | `POST /api/pagamento` (ver [pagamento.md](pagamento.md)) |
| Aprovar sem pagar (opcional) | `PATCH /api/orcamento/{id}/aprovar` |
| Recusar | `PATCH /api/orcamento/{id}/recusar` com `{ "motivo": "..." }` |
| PDF | `GET /api/orcamento/{id}/pdf` |

Pagamento, aprovação e recusa de orçamento recusado, cancelado ou já pago respondem `409`.

## Infra (OrcamentoPagamento-InfraKubernete)

- RabbitMQ: o do namespace `mensageria` do cluster, instalado pelo InfraKubernete
  (`rabbitmq.mensageria.svc.cluster.local:5672`). Usuário e senha vêm do SSM
  (`/<projeto>/<ambiente>/mensageria/rabbitmq-user` e `rabbitmq-segredo`) para o Secret
  `orcamento-secrets`, como no OS Service.
- `Mensageria__Habilitada` no ConfigMap liga a Saga; sem as credenciais do RabbitMQ no SSM
  o deploy a mantém desligada.
- SNS, SQS, DLQ, os parâmetros de tópico/fila no SSM e as permissões IAM de SNS/SQS saíram
  do Terraform.
