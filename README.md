# OrcamentoPagamento-service

Microsserviço de **orçamentos** da oficina MechanicLtda, em **.NET 9**, seguindo os mesmos
princípios de DDD e Clean Architecture do repositório principal.

Extraído do monólito [MechanicLtda](https://github.com/Fiap-Mecanic-Ltda/MechanicLtda), é o
**Billing** da Saga da Ordem de Serviço: recebe o `GerarOrcamento` do
[ordemservico-service](https://github.com/Fiap-Mecanic-Ltda/ordemservico-service) pelo
**RabbitMQ**, calcula o orçamento no **próprio banco**, responde `OrcamentoGerado` e avisa a
Saga quando o cliente paga (`PagamentoConfirmado`) ou recusa. Também expõe consulta, ajuste
manual e exportação em PDF. Contrato das mensagens: [docs/contratos-eventos.md](docs/contratos-eventos.md).

O **pagamento** vive no mesmo serviço, como módulo separado, integrado ao **Mercado Pago**
via Checkout Transparente, na variante **Payments API** (`POST /v1/payments`) — com a
dívida técnica de que a Orders API é a recomendada para integração nova, registrada em
[docs/pagamento.md § 10](docs/pagamento.md).

> **Estado atual:** orçamento e pagamento **implementados e testados** (145 testes). O
> contrato das APIs de pagamento está em [docs/pagamento.md](docs/pagamento.md), que é o
> que o time do Web usa para codar a chamada. A integração roda apenas com credenciais de
> teste, sem movimentação de dinheiro real.

## Onde este serviço entra

```text
                                   ┌──────── ordemservico-service (orquestrador) ────────┐
Browser ─▶ Web (Razor)             │  Saga da OS: Recebida → ... → AguardandoAprovacao   │
              │                    └───────────────┬─────────────────────▲───────────────┘
              │                       GerarOrcamento│ CancelarOrcamento   │ OrcamentoGerado/Falhou
              │                                     │ EstornarPagamento   │ PagamentoConfirmado/Falhou
              │ HttpClient                          ▼                     │ OrcamentoAprovado/Recusado
              ▼                         ┌──────── RabbitMQ (exchanges MechanicLtda.Contratos:*) ─┐
    API Gateway (HTTP API)              │ billing-gerar-orcamento, billing-cancelar-orcamento,   │
      ├─ /api/orcamento/* ──┐           │ billing-estornar-pagamento                             │
      ├─ /api/pagamento/* ──┤           └───────────────┬─────────────────────▲──────────────────┘
      └─ /api/*  ─▶ outros  │                           ▼                     │
                            ▼          ┌─── OrcamentoPagamento-service (este repo) ───┐
                            └─────────▶│  Orcamento  REST: consulta, ajuste, PDF,      │
                                       │             aprovar/recusar                  │
                                       │             Consumidores da Saga             │
                                       │  Pagamento  REST: cobranca + webhook         │
                                       │  Banco proprio: MechanicOrcamentoDb          │
                                       └──────────────────────┬───────────────────────┘
                                                              │ Checkout API / refunds
                                                              ▼
                                                        Mercado Pago ──webhook──▶ de volta
```

Duas portas de entrada para o cálculo:

- **Saga (principal):** `GerarOrcamento` na fila `billing-gerar-orcamento`, quando o
  diagnóstico termina no OS Service. Ver [ADR-004](docs/adr/ADR-004-saga-rabbitmq.md).
- **Manual:** `POST /api/orcamento/recalcular`. Serve para operar e testar sem a Saga; o
  orçamento criado assim não tem `correlationId` e não publica nada na Saga.

## Arquitetura

```text
src/
├── OrcamentoPagamento.API            # Host REST, Swagger, JWT, webhook, registro do MassTransit
├── OrcamentoPagamento.Application    # AppServices, DTOs, PDF
├── OrcamentoPagamento.Contratos      # Mensagens da Saga (namespace MechanicLtda.Contratos, copia do OS Service)
├── OrcamentoPagamento.Domain         # Orcamento + Pagamento + Saga: entidades e regras
└── OrcamentoPagamento.Infrastructure # EF Core, repositorios, consumidores/publicador RabbitMQ, gateway MercadoPago
tests/
├── OrcamentoPagamento.Domain.Tests         # Regra de calculo, idempotencia, ajuste manual
└── OrcamentoPagamento.API.IntegrationTests # Endpoints, autorizacao, fluxo HTTP completo
```

Dependências: `API → Application → Domain` e `API → Infrastructure → Domain`; o `Domain` só
depende do `Contratos`, que não tem dependência nenhuma. `Application` e `Infrastructure` não
dependem uma da outra.

Não há projeto `Bootstrap` como no monólito: há um único host, então a composição fica nas
extensões do próprio `API`.

> A regra da Saga fica no `Domain` (`SagaOrcamentoService`); os consumidores do MassTransit na
> `Infrastructure` só a chamam, um por fila.

## Modelo de domínio

| Entidade | Descrição |
|---|---|
| `Orcamento` | Agregado raiz. Totais por categoria, validade, marca de ajuste manual, status na Saga e o `CorrelationId` da Saga que o gerou (idempotência) |
| `ItemOrcamento` | Cópia local do item que originou o orçamento, já classificado |
| `OrdemServicoSnapshot` | Dados da OS, do cliente e do veículo no momento do cálculo — é o que alimenta o PDF |
| `Pagamento` | Tentativa de cobrança de um orçamento. Valor congelado, máquina de estados, e idempotência por requisição e por notificação |

`Orcamento.OrdemServicoId` **não tem chave estrangeira**: a OS vive no banco de outro
serviço. Um índice único garante "uma OS, um orçamento" aqui dentro.

| Enum | Valores |
|---|---|
| `CategoriaItem` | `1 = Peca`, `2 = Insumo`, `3 = Servico` (na Saga chega como texto em `itens[].tipo`) |
| `StatusOrcamento` | `1 = Aberto`, `2 = Aprovado`, `3 = Pago`, `4 = Recusado`, `5 = Cancelado`. Só `Aberto` e `Aprovado` aceitam pagamento |
| `StatusPagamento` | `1 = Pendente`, `2 = EmProcessamento`, `3 = Autorizado`, `4 = Aprovado`, `5 = Recusado`, `6 = Cancelado`, `7 = Devolvido`, `8 = EmDisputa`, `9 = Estornado` |

### Regra de cálculo

Os totais saem dos itens recebidos no evento: `ValorTotalPecas`, `ValorTotalInsumos` e
`ValorTotalServicos` por categoria; `ValorTotalGeral` é a **soma de todos os itens** (e não
das três categorias), para continuar fechando se uma categoria nova aparecer. A validade
padrão é de 30 dias, como no monólito.

Um recálculo **sobrescreve** um ajuste manual anterior — mesmo comportamento do monólito. A
marca `AjustadoManualmente` fica para auditoria.

## Endpoints

Todas as rotas exigem JWT com role `Administrador` ou `Funcionario`, exceto `/health` e o
webhook do Mercado Pago.

### Orçamento

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/orcamento/{id}` | Orçamento pelo Id, com itens |
| `GET` | `/api/orcamento/ordem-servico/{ordemServicoId}` | Orçamento de uma Ordem de Serviço |
| `PUT` | `/api/orcamento/{id}` | Ajuste manual de valores |
| `DELETE` | `/api/orcamento/{id}` | Remove o orçamento |
| `GET` | `/api/orcamento/{id}/pdf` | Exporta o orçamento em PDF |
| `POST` | `/api/orcamento/recalcular` | Recálculo manual, fora da Saga |
| `PATCH` | `/api/orcamento/{id}/aprovar` | Aprovação sem pagamento: publica `OrcamentoAprovado` (só informativo) |
| `PATCH` | `/api/orcamento/{id}/recusar` | Recusa do cliente (`{ "motivo": "..." }`): publica `OrcamentoRecusado` e cancela pagamentos em aberto |
| `GET` | `/health` | Healthcheck (público) |

`POST /api/orcamento/recalcular` responde `204 No Content` quando o `eventoId` já havia sido
aplicado — é descarte por idempotência, não erro. `aprovar`, `recusar` e `POST /api/pagamento`
respondem `409` quando o orçamento foi recusado, cancelado ou já pago.

### Saga da OS (RabbitMQ)

| Fila | Recebe | Faz | Responde |
|---|---|---|---|
| `billing-gerar-orcamento` | `GerarOrcamento` | Calcula e grava o orçamento (status `Aberto`) | `OrcamentoGerado` ou `OrcamentoFalhou` |
| `billing-cancelar-orcamento` | `CancelarOrcamento` | Status `Cancelado`, cancela pagamentos em aberto | `OrcamentoCancelado` |
| `billing-estornar-pagamento` | `EstornarPagamento` | Devolução total no Mercado Pago (ou cancela, se ainda em aberto) | `PagamentoEstornado` |

Pagamento aprovado publica `PagamentoConfirmado` (o orçamento vira `Pago` e a OS vai para
Execução); recusado publica `PagamentoFalhou`. Regras, idempotência e erros em
[ADR-004](docs/adr/ADR-004-saga-rabbitmq.md).

As respostas usam o mesmo envelope `ResponseDto` do monólito (`hasErrors`, `getErrors`,
`getResponse`), de propósito: o host Web lê esse formato hoje e não precisa mudar as views.

### Pagamento

Contrato completo — payloads, códigos de resposta, máquina de estados e plano de teste —
em [docs/pagamento.md](docs/pagamento.md).

| Método | Rota | Quem chama | Descrição |
|---|---|---|---|
| `POST` | `/api/pagamento` | Web | Cria o pagamento a partir do token do cartão |
| `GET` | `/api/pagamento/{id}` | Web | Consulta um pagamento |
| `GET` | `/api/pagamento/orcamento/{orcamentoId}` | Web | Pagamentos de um orçamento |
| `GET` | `/api/pagamento/ordem-servico/{ordemServicoId}` | Web | Pagamentos de uma OS |
| `POST` | `/api/pagamento/{id}/sincronizar` | Web / operação | Força `GET` no Mercado Pago e reaplica o status |
| `POST` | `/api/webhooks/mercadopago` | Mercado Pago | Notificação de mudança. Público, com validação de `x-signature` |

Dois pontos do contrato que valem destaque:

- **O valor não vem na requisição.** O `transaction_amount` é lido do
  `Orcamento.ValorTotalGeral` no servidor — senão o cliente poderia pagar R$ 0,01 por um
  orçamento de R$ 450.
- **O dado do cartão nunca passa por este backend.** O Web tokeniza com o SDK JS do
  Mercado Pago e manda só o `token`.

Swagger: `/swagger` (local `http://localhost:5080/swagger`, Docker `http://localhost:8083/swagger`).

## Autenticação

O serviço **não emite tokens**. Valida os JWT emitidos pela API do monólito (login por
e-mail e senha) e pela Lambda de autenticação por CPF — mesma chave simétrica
(`JWT_SECRET_KEY`) e os mesmos dois emissores (`JwtSettings:Issuer` e
`JwtSettings:IssuerCpf`).

## Configuração

| Chave | Uso |
|---|---|
| `ConnectionStrings:DefaultConnection` | Banco do serviço (`MechanicOrcamentoDb`) |
| `JWT_SECRET_KEY` | Chave de validação dos tokens. Variável de ambiente ou configuração |
| `JwtSettings:Issuer` / `IssuerCpf` / `Audience` | Emissores e audiência aceitos |
| `Mensageria:Habilitada` | `true` liga a Saga (consumidores e publicação no RabbitMQ). `false` (padrão) sobe sem broker e só registra as mensagens em log |
| `Mensageria:Transporte` | `RabbitMq` (padrão) ou `InMemory` (testes) |
| `RabbitMq:Host` / `Porta` / `VirtualHost` | Broker. No cluster, `rabbitmq.mensageria.svc.cluster.local:5672` |
| `RabbitMq:Usuario` / `Senha` | Credenciais do broker. No cluster, do SSM via Secret |
| `Pagamentos:Habilitado` | `false` usa gateway fake, sem tocar no Mercado Pago. Padrão em dev e nos testes |
| `Pagamentos:MercadoPago:AccessToken` | Access token **de teste**. Via `MERCADOPAGO_ACCESS_TOKEN` ou SSM — nunca em `appsettings` |
| `Pagamentos:MercadoPago:WebhookSecret` | Secret da validação de `x-signature` do webhook |
| `Pagamentos:MercadoPago:BaseUrl` | Padrão `https://api.mercadopago.com` |
| `Pagamentos:MercadoPago:NotificationUrl` | URL pública do webhook, registrada no painel do Mercado Pago |

## Como executar

### Localmente

```bash
dotnet restore
dotnet build
dotnet run --project src/OrcamentoPagamento.API
```

Precisa de um SQL Server acessível e de `JWT_SECRET_KEY` no ambiente:

```powershell
$env:JWT_SECRET_KEY = "sua-chave-jwt-secreta-com-no-minimo-32-caracteres"
```

Com `Mensageria:Habilitada = false` (padrão), o serviço sobe sem RabbitMQ e só responde pelo
REST; as mensagens da Saga ficam no log. As migrations são aplicadas automaticamente na subida.

### Com Docker Compose (RabbitMQ + banco do InfraSGBD)

A infraestrutura fica em repositórios próprios:

| Repositório | Conteúdo |
|---|---|
| [OrcamentoPagamento-InfraSGBD](https://github.com/Fiap-Mecanic-Ltda/OrcamentoPagamento-InfraSGBD) | SQL Server local e Terraform do RDS (`terraform plan/apply`) |
| [OrcamentoPagamento-InfraKubernete](https://github.com/Fiap-Mecanic-Ltda/OrcamentoPagamento-InfraKubernete) | Terraform do ECR, ALB, IAM e SSM + manifestos `k8s/` e deploy no cluster |

O banco sobe primeiro, no InfraSGBD, porque é ele que cria a rede `orcamento-net` usada
pela API. Use o **mesmo `SA_PASSWORD`** nos dois `.env`.

Para validar a Saga ponta a ponta, este serviço usa o **RabbitMQ do OS Service** (5672 no
host). No `.env` do ordemservico-service, desligue o simulador do Billing
(`SIMULADOR_BILLING=false`); aqui, use o mesmo usuário e senha do RabbitMQ de lá:

```bash
# 1. OS Service (SQL Server, RabbitMQ e API na 8081), com SIMULADOR_BILLING=false
cd ../ordemservico-service && docker compose up -d --build

# 2. Banco deste serviço (repositório OrcamentoPagamento-InfraSGBD)
cd ../OrcamentoPagamento-InfraSGBD
cp .env.example .env   # ajuste SA_PASSWORD
docker compose up -d

# 3. Este serviço (API na 8083)
cd ../OrcamentoPagamento-service
cp .env.example .env   # SA_PASSWORD, JWT_SECRET_KEY e RABBITMQ_USER/RABBITMQ_PASSWORD do OS Service
docker compose up --build
```

Para rodar sozinho, com um RabbitMQ próprio (portas 5674/15674 no host):

```bash
RABBITMQ_HOST=rabbitmq docker compose --profile rabbitmq up --build
```

No painel do RabbitMQ devem aparecer as filas `billing-gerar-orcamento`,
`billing-cancelar-orcamento` e `billing-estornar-pagamento`, ligadas aos exchanges
`MechanicLtda.Contratos:GerarOrcamento`, `:CancelarOrcamento` e `:EstornarPagamento`.

Para testar sem o OS Service, publique um `GerarOrcamento` no exchange pelo painel
(*Exchanges > MechanicLtda.Contratos:GerarOrcamento > Publish message*), com o cabeçalho
`correlation-id` e o corpo do exemplo de [docs/contratos-eventos.md](docs/contratos-eventos.md).
Depois, com um token válido:

```bash
curl -H "Authorization: Bearer $TOKEN" http://localhost:8083/api/orcamento/ordem-servico/42
```

Comando que falha depois das tentativas vai para a fila `_error` da fila de origem (ex.:
`billing-gerar-orcamento_error`).

Para parar o serviço. O banco continua de pé no InfraSGBD; para zerá-lo, rode
`docker compose down -v` lá:

```bash
docker compose down
```

### Testando o pagamento

Dois modos, escolhidos por `PAGAMENTOS_HABILITADO` no `.env`. Nenhum arquivo de código muda
entre eles.

#### Modo fake — padrão, nada a configurar

É o que vale sem `.env` e sem credencial nenhuma. O resultado é decidido pelo **token** que
você envia, com os mesmos códigos de quatro letras que o Mercado Pago usa no nome do titular
do cartão de teste:

| `token` enviado | Resultado |
|---|---|
| `APRO` (ou qualquer outro) | `Aprovado` |
| `CONT` | `Pendente` — e a consulta seguinte aprova, o que exercita webhook e máquina de estados |
| `FUND` | `Recusado`, `cc_rejected_insufficient_amount` |
| `SECU`, `CALL`, `EXPI`, `OTHE` | `Recusado`, com `status_detail` diferente em cada |

```bash
# 1. Orcamento de R$ 450 para a OS 12
curl -X POST http://localhost:8083/api/orcamento/recalcular \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"eventoId":"'$(uuidgen)'","ocorridoEm":"2026-11-11T12:00:00Z","ordemServicoId":12,
       "itens":[{"itemId":1,"descricao":"Pastilha","categoria":1,"quantidade":2,"valorUnitario":100},
                {"itemId":2,"descricao":"Oleo","categoria":2,"quantidade":2,"valorUnitario":50},
                {"itemId":3,"descricao":"Mao de obra","categoria":3,"quantidade":1,"valorUnitario":150}]}'

# 2. Cobranca que nasce pendente. Repare que nao se manda valor: ele vem do orcamento.
curl -X POST http://localhost:8083/api/pagamento \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"requisicaoId":"'$(uuidgen)'","orcamentoId":1,"token":"CONT","paymentMethodId":"master",
       "installments":1,"payer":{"email":"comprador@testuser.com",
       "identification":{"type":"CPF","number":"12345678909"}}}'

# 3. Fecha o ciclo sem webhook: Pendente -> Aprovado
curl -X POST http://localhost:8083/api/pagamento/1/sincronizar -H "Authorization: Bearer $TOKEN"
```

#### Modo Mercado Pago

Ainda em teste, sem dinheiro real. No `.env`:

```env
PAGAMENTOS_HABILITADO=true
MERCADOPAGO_ACCESS_TOKEN=TEST-...
```

Para `dotnet run` local, o token **não** vai no `.env` — vai em User Secrets, para não
correr risco de ser commitado:

```bash
dotnet user-secrets set "Pagamentos:MercadoPago:AccessToken" "TEST-..." \
  --project src/OrcamentoPagamento.API
```

O que é preciso obter antes, no painel do desenvolvedor:

1. **Criar a aplicação.**
2. **Copiar as credenciais de teste** (*Detalhes da aplicação > Credenciais*): o access
   token fica com este serviço, a public key com o time do Web. **Têm de ser da mesma
   aplicação** — token de cartão gerado pela public key de uma aplicação é rejeitado pelo
   access token de outra.
3. **Webhook secret e `notification_url`** só fazem sentido quando houver URL pública —
   `localhost` não recebe notificação. Até então, `POST /api/pagamento/{id}/sincronizar`
   faz o mesmo trabalho sob demanda.

É só isso. **Não é preciso criar conta de teste** para este fluxo: as credenciais de teste
saem da sua própria conta de produção, e o pagador é identificado só por e-mail e CPF — o
único requisito é que o `payer.email` seja **diferente do e-mail da sua conta** Mercado Pago.
Conta de teste serve para simular quem faz *login* no Mercado Pago (Checkout Pro, carteira,
saldo em conta) ou para olhar o lado do vendedor sem usar a conta real; nada disso entra
aqui.

Cartões de teste e os códigos que forçam cada resultado estão em
[docs/pagamento.md § 5](docs/pagamento.md).

## Testes

```bash
dotnet test
```

| Projeto | Cobertura |
|---|---|
| `OrcamentoPagamento.Domain.Tests` (107) | **Orçamento:** totais por categoria, idempotência da rota manual, snapshot, ajuste manual, publicação e falha na publicação. **Pagamento:** valor vindo do orçamento, idempotência por `requisicaoId`, recusa como resultado, máquina de estados completa, notificação atrasada, cancelamento por valor alterado, `PagamentoConfirmado`/`PagamentoFalhou` na Saga, orçamento encerrado. **Saga:** `GerarOrcamento` (tipo texto, repetição, Saga nova, itens inválidos), cancelamento, estorno (devolução, pendente, repetição, provedor fora), aprovação e recusa |
| `OrcamentoPagamento.API.IntegrationTests` (38) | Autenticação e autorização por role, fluxo recalcular → consultar → ajustar → PDF, criação de pagamento (`APRO`/`FUND`/`CONT`), `409` em orçamento já pago, sincronização, webhook público e repetido, recálculo cancelando pendente. **Saga com o MassTransit em memória:** `GerarOrcamento` pelo bus, pagamento até `Pago`, compensação (estorno e cancelamento), recusa bloqueando pagamento, aprovação |

Os testes de integração sobem a API real com `WebApplicationFactory`, banco InMemory,
mensageria desligada (ou no transporte em memória do MassTransit, nos testes da Saga) e **gateway de pagamento fake** — a CI não depende do sandbox do
Mercado Pago.

O gateway fake decide o resultado pelo **token**, com os mesmos códigos de quatro letras
do Mercado Pago: `APRO` aprova, `FUND` recusa por saldo, `CONT` deixa pendente (e a
consulta seguinte aprova, o que permite exercitar webhook e máquina de estados sem o
provedor).

## Migrations

O `dotnet-ef` está fixado como ferramenta local do repositório:

```bash
dotnet tool restore

dotnet ef migrations add NomeDaMigration \
  --project src/OrcamentoPagamento.Infrastructure \
  --startup-project src/OrcamentoPagamento.API \
  --output-dir Migrations
```

## CI/CD

`.github/workflows/main.yml`, no mesmo contrato dos outros repositórios de aplicação:

- PR e push em `desenvolvimento`, `homologacao` e `main`: build e testes.
- push em `main`: publica a imagem no ECR (`mechanicltda-orcamento`, tags `latest` e SHA) e
  dispara o workflow de deploy no
  [OrcamentoPagamento-InfraKubernete](https://github.com/Fiap-Mecanic-Ltda/OrcamentoPagamento-InfraKubernete).

| Repositório | Conteúdo | Pipeline |
|---|---|---|
| [InfraKubernete](https://github.com/Fiap-Mecanic-Ltda/InfraKubernete) | Cluster compartilhado (VPC, EC2/k3s, ASG, ALB, IAM/OIDC) | `terraform plan/apply` |
| [OrcamentoPagamento-InfraKubernete](https://github.com/Fiap-Mecanic-Ltda/OrcamentoPagamento-InfraKubernete) | Terraform do serviço no cluster (ECR, ALB, IAM, SSM) + manifestos `k8s/` | `terraform plan/apply` e deploy no cluster |
| [OrcamentoPagamento-InfraSGBD](https://github.com/Fiap-Mecanic-Ltda/OrcamentoPagamento-InfraSGBD) | Terraform do RDS SQL Server (instância, subnet group, SG, connection string no SSM) | `terraform plan/apply` |
| **OrcamentoPagamento-service** (este repo) | Aplicação .NET 9 (`src/`, `tests/`, Dockerfile) | build, testes e push da imagem no ECR |

Secrets: `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` (checagem da infra), `AWS_ROLE_ARN`
(OIDC) e `INFRA_DISPATCH_TOKEN` (fine-grained, Contents: read/write no
OrcamentoPagamento-InfraKubernete).

Autenticação na AWS por **OIDC**, sem access keys estáticas.

Com o secret `SONAR_TOKEN`, a esteira analisa o código no SonarCloud. No PR, a esteira espera o quality gate e falha se ele reprovar. No push na `main`, quem confere o gate é o `scripts/quality-gate-sonar.py`: ele falha com `ERROR` e só avisa quando o gate volta `NONE` ou não está acessível. No plano gratuito, o SonarCloud nem sempre calcula o gate na `main`, e isso travava a publicação da imagem.

## Documentação

| Documento | Assunto |
|---|---|
| [Pagamento](docs/pagamento.md) | **Contrato das APIs de pagamento** (Mercado Pago), máquina de estados, modo de teste |
| [Alinhamento e discovery](docs/alinhamento-e-discovery.md) | Documento vivo: o que cada time assume do outro, decisões e perguntas abertas |
| [Implementação](docs/implementacao.md) | Inventário do que existe e de onde cada parte veio |
| [ADR-001](docs/adr/ADR-001-extracao-do-orcamento-como-microsservico.md) | Por que extrair o orçamento e por que banco por serviço |
| [Contratos da Saga](docs/contratos-eventos.md) | Mensagens da Saga da OS (cópia idêntica nos três serviços) |
| [ADR-002](docs/adr/ADR-002-contrato-de-eventos.md) | *(substituída pela ADR-004)* Evento SNS/SQS de itens alterados |
| [ADR-003](docs/adr/ADR-003-consumo-pelo-web-via-api-gateway.md) | Como o host Web consome este serviço |
| [ADR-004](docs/adr/ADR-004-saga-rabbitmq.md) | Participação na Saga da OS pelo RabbitMQ (substitui SNS/SQS) |
| [Integração](docs/integracao.md) | Como este serviço conversa com OS Service, Web e infra; validação local |
| [Postman](docs/postman/README.md) | Coleção com o fluxo completo do orçamento |
