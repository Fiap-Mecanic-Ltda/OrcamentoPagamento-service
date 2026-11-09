# OrcamentoPagamento-service

Microsserviço de **orçamentos** da oficina MechanicLtda, em **.NET 9**, seguindo os mesmos
princípios de DDD e Clean Architecture do repositório principal.

Extraído do monólito [MechanicLtda](https://github.com/Fiap-Mecanic-Ltda/MechanicLtda):
consome os eventos de alteração de itens da Ordem de Serviço, calcula o orçamento no
**próprio banco** e expõe consulta, ajuste manual e exportação em PDF.

O **pagamento** vive no mesmo serviço, como módulo separado, integrado ao **Mercado Pago**
via Checkout API.

> **Estado atual:** orçamento e pagamento **implementados e testados** (102 testes). O
> contrato das APIs de pagamento está em [docs/pagamento.md](docs/pagamento.md), que é o
> que o time do Web usa para codar a chamada. A integração roda apenas com credenciais e
> contas de teste, sem movimentação de dinheiro real.

## Onde este serviço entra

```text
                      ┌──────────────── MechanicLtda (monolito) ───────────────┐
Browser ─▶ Web (Razor)│  API: Cliente, Veiculo, OrdemServico, Item, Estoque    │
              │       └────────────────────────┬───────────────────────────────┘
              │                                │ publica (itens alterados)
              │ HttpClient                     ▼
              ▼                        SNS ordem-servico-eventos
    API Gateway (HTTP API)                      │
      ├─ /api/orcamento/* ──┐          SQS ordem-servico-itens-alterados ──▶ DLQ
      └─ /api/*  ─▶ monolito│                   │
                            ▼                   ▼
                   ┌─── OrcamentoPagamento-service (este repo) ───┐
                   │  Orcamento  REST: consulta, ajuste, PDF      │
                   │             Consumer: recalcula do evento    │
                   │  Pagamento  REST: cobranca + webhook         │
                   │  Banco proprio: MechanicOrcamentoDb          │
                   └───────┬──────────────────────────┬───────────┘
                           │ publica                  │ Checkout API
                           ▼                          ▼
                 SNS orcamento-eventos          Mercado Pago
                                                      │ webhook
                                                      └──▶ de volta ao servico
```

Duas portas de entrada para o mesmo cálculo:

- **Assíncrona (principal):** mensagem na fila SQS, disparada pela alteração de itens da OS.
- **Síncrona (alternativa):** `POST /api/orcamento/recalcular`, com o mesmo payload. Serve
  para reprocessar mensagem que caiu na DLQ, operar com a mensageria desligada, ou quando o
  chamador precisa do orçamento na mesma requisição.

## Arquitetura

```text
src/
├── OrcamentoPagamento.API            # Host REST, Swagger, JWT, consumidor SQS, webhook
├── OrcamentoPagamento.Application    # AppServices, DTOs, contrato dos eventos, PDF
├── OrcamentoPagamento.Domain         # Orcamento + Pagamento: entidades e regras
└── OrcamentoPagamento.Infrastructure # EF Core, repositorios, SNS, gateway MercadoPago
tests/
├── OrcamentoPagamento.Domain.Tests         # Regra de calculo, idempotencia, ajuste manual
└── OrcamentoPagamento.API.IntegrationTests # Endpoints, autorizacao, fluxo HTTP completo
```

Dependências: `API → Application → Domain` e `API → Infrastructure → Domain`. O `Domain`
não depende de ninguém; `Application` e `Infrastructure` não dependem uma da outra.

Não há projeto `Bootstrap` como no monólito: há um único host, então a composição fica nas
extensões do próprio `API`.

> O consumidor SQS mora no host (`API/Messaging/`), e não na `Infrastructure`, porque
> precisa da camada de `Application` — e `Infrastructure` depende só do `Domain`.

## Modelo de domínio

| Entidade | Descrição |
|---|---|
| `Orcamento` | Agregado raiz. Totais por categoria, validade, marca de ajuste manual e o último evento aplicado (idempotência) |
| `ItemOrcamento` | Cópia local do item que originou o orçamento, já classificado |
| `OrdemServicoSnapshot` | Dados da OS, do cliente e do veículo no momento do cálculo — é o que alimenta o PDF |
| `Pagamento` | Tentativa de cobrança de um orçamento. Valor congelado, máquina de estados, e idempotência por requisição e por notificação |

`Orcamento.OrdemServicoId` **não tem chave estrangeira**: a OS vive no banco de outro
serviço. Um índice único garante "uma OS, um orçamento" aqui dentro.

| Enum | Valores |
|---|---|
| `CategoriaItem` | `1 = Peca`, `2 = Insumo`, `3 = Servico` |
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
| `POST` | `/api/orcamento/recalcular` | Recalcula a partir do payload do evento |
| `GET` | `/health` | Healthcheck (público) |

`POST /api/orcamento/recalcular` responde `204 No Content` quando o evento já havia sido
aplicado — é descarte por idempotência, não erro.

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

Swagger: `/swagger` (local `http://localhost:5080/swagger`, Docker `http://localhost:8081/swagger`).

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
| `Messaging:Habilitado` | `false` desliga publicação e consumo (só log). Padrão em dev e nos testes |
| `Messaging:TopicoEventosArn` | Tópico SNS onde os eventos do orçamento são publicados |
| `Messaging:FilaItensAlteradosUrl` | Fila SQS dos eventos de itens alterados |
| `Messaging:ServiceUrl` | Endpoint alternativo do SNS/SQS (LocalStack) |
| `Messaging:Regiao` | Região da AWS. Padrão `us-east-1` |
| `Messaging:VisibilityTimeoutSegundos` | Tempo de invisibilidade da mensagem em processamento. Padrão 60 |
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

Com `Messaging:Habilitado = false` (padrão), o serviço sobe sem tocar na AWS e só responde
pelo REST. As migrations são aplicadas automaticamente na subida.

### Com Docker Compose (SQL Server + LocalStack)

```bash
cp .env.example .env   # ajuste SA_PASSWORD e JWT_SECRET_KEY
docker compose up --build
```

Isso sobe:

1. **SQL Server 2022** na porta `1434` do host — `1433` fica livre para o banco do monólito.
2. **LocalStack** (SNS + SQS) na `4566`, já com tópicos, fila e DLQ criados por
   `scripts/localstack/01-criar-topico-e-fila.sh`.
3. **O microsserviço** na `8081`, com a mensageria ligada apontando para o LocalStack.

Publicar um evento de teste:

```bash
docker exec orcamento-localstack awslocal sns publish \
  --topic-arn arn:aws:sns:us-east-1:000000000000:ordem-servico-eventos \
  --message-attributes '{"tipoEvento":{"DataType":"String","StringValue":"ordem-servico.itens-alterados"}}' \
  --message '{
    "eventoId":"11111111-1111-1111-1111-111111111111",
    "tipoEvento":"ordem-servico.itens-alterados",
    "ocorridoEm":"2026-11-06T14:32:10Z",
    "ordemServicoId":12,
    "ordemServico":{"descricaoProblema":"Revisao dos 20 mil km","status":"EmDiagnostico","clienteNome":"Carlos Oliveira","clienteEmail":"carlos@email.com","veiculoMarca":"Toyota","veiculoModelo":"Corolla","veiculoPlaca":"ABC1D23","veiculoAno":2021},
    "itens":[
      {"itemId":41,"descricao":"Pastilha de freio","categoria":1,"quantidade":2,"valorUnitario":100.00},
      {"itemId":42,"descricao":"Oleo 5W30","categoria":2,"quantidade":2,"valorUnitario":50.00},
      {"itemId":43,"descricao":"Mao de obra","categoria":3,"quantidade":1,"valorUnitario":150.00}
    ]
  }'
```

Depois, com um token válido:

```bash
curl -H "Authorization: Bearer $TOKEN" http://localhost:8081/api/orcamento/ordem-servico/12
```

Mensagens que falham no processamento acabam na DLQ:

```bash
docker exec orcamento-localstack awslocal sqs receive-message \
  --queue-url http://localhost:4566/000000000000/ordem-servico-itens-alterados-dlq
```

Para parar, e para apagar também o volume do banco:

```bash
docker compose down
docker compose down -v
```

## Testes

```bash
dotnet test
```

| Projeto | Cobertura |
|---|---|
| `OrcamentoPagamento.Domain.Tests` (70) | **Orçamento:** totais por categoria, idempotência (evento repetido e atrasado), snapshot, ajuste manual, publicação e falha na publicação. **Pagamento:** valor vindo do orçamento, idempotência por `requisicaoId`, recusa como resultado, máquina de estados completa, notificação atrasada, cancelamento por valor alterado |
| `OrcamentoPagamento.API.IntegrationTests` (32) | Autenticação e autorização por role, fluxo recalcular → consultar → ajustar → PDF, criação de pagamento (`APRO`/`FUND`/`CONT`), `409` em orçamento já pago, sincronização, webhook público e repetido, recálculo cancelando pendente |

Os testes de integração sobem a API real com `WebApplicationFactory`, banco InMemory,
mensageria desligada e **gateway de pagamento fake** — a CI não depende do sandbox do
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
  dispara o workflow de deploy no [InfraKubernete](https://github.com/Fiap-Mecanic-Ltda/InfraKubernete).

Autenticação na AWS por **OIDC**, sem access keys estáticas.

## Documentação

| Documento | Assunto |
|---|---|
| [Pagamento](docs/pagamento.md) | **Contrato das APIs de pagamento** (Mercado Pago), máquina de estados, modo de teste |
| [Alinhamento e discovery](docs/alinhamento-e-discovery.md) | Documento vivo: o que cada time assume do outro, decisões e perguntas abertas |
| [Implementação](docs/implementacao.md) | Inventário do que existe e de onde cada parte veio |
| [ADR-001](docs/adr/ADR-001-extracao-do-orcamento-como-microsservico.md) | Por que extrair o orçamento e por que banco por serviço |
| [ADR-002](docs/adr/ADR-002-contrato-de-eventos.md) | Contrato do evento, idempotência e tratamento de erro no consumo |
| [ADR-003](docs/adr/ADR-003-consumo-pelo-web-via-api-gateway.md) | Como o host Web consome este serviço |
| [Integração](docs/integracao.md) | O que ainda falta nos outros repositórios |
| [Postman](docs/postman/README.md) | Coleção com o fluxo completo do orçamento |
