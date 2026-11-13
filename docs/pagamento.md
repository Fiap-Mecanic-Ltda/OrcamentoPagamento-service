# Pagamento via Mercado Pago — contrato do microsserviço

- **Status:** **implementado e testado** (102 testes na solução, 53 deles de pagamento)
- **Data:** 2026-11-06
- **Modo:** apenas credenciais de teste. **Sem movimentação de dinheiro real.**
- **Integração escolhida:** Checkout Transparente **via Payments API** — `POST /v1/payments`
- **Dívida técnica conhecida:** para integração nova, o Mercado Pago recomenda a **Orders
  API**. Ficamos na Payments de propósito — ver [§ 10](#10-dívida-técnica-payments-api-vs-orders-api)

Este documento define as APIs e as chamadas do microsserviço para o fluxo de pagamento. O
pagamento fica **no mesmo serviço do orçamento**, como módulo separado: agregado próprio,
tabelas próprias, nenhuma mistura com o agregado `Orcamento`.

Serve a dois públicos: o time que está fazendo a chamada no Web, que precisa do contrato
de entrada; e quem for implementar aqui depois.

## 1. Divisão de responsabilidade

Com Checkout API, o dado do cartão **nunca passa pelo nosso backend**. Isso não é detalhe
de implementação, é o que mantém a superfície de PCI fora deste serviço.

| Etapa | Quem faz | O que acontece |
|---|---|---|
| 1. Coleta e tokenização do cartão | **Web** (outro time) | SDK JS do Mercado Pago, `mp.fields.createCardToken()`. Devolve um `token` de uso único e vida curta |
| 2. Criação do pagamento | **Este microsserviço** | Recebe o `token`, chama `POST https://api.mercadopago.com/v1/payments` com o access token e `X-Idempotency-Key` |
| 3. Persistência e resposta | **Este microsserviço** | Grava o `Pagamento`, devolve status ao Web |
| 4. Atualização de status | **Este microsserviço** | Recebe o webhook do Mercado Pago, faz `GET /v1/payments/{id}` e aplica a transição |

```text
  Web (outro time)                 Este microsservico              Mercado Pago
        │                                  │                             │
        │ 1. createCardToken() ───────────────────────────────────────▶   │
        │ ◀─────────────────────────────────────────── token (uso unico)  │
        │                                  │                             │
        │ 2. POST /api/pagamento ────────▶ │                             │
        │    { orcamentoId, token, ... }   │ 3. POST /v1/payments ─────▶  │
        │                                  │ ◀──── { id, status }         │
        │ ◀──── { status, idExterno } ──── │                             │
        │                                  │                             │
        │                                  │ ◀── 4. POST webhook ──────── │
        │                                  │ 5. GET /v1/payments/{id} ─▶  │
        │                                  │ ◀──── status confirmado      │
```

## 2. APIs expostas por este microsserviço

Todas sob `Authorization: Bearer <jwt>` com role `Administrador` ou `Funcionario` — exceto
o webhook, que é público por natureza.

| Método | Rota | Quem chama | Descrição |
|---|---|---|---|
| `POST` | `/api/pagamento` | Web | Cria o pagamento a partir do token do cartão |
| `GET` | `/api/pagamento/{id}` | Web | Consulta um pagamento |
| `GET` | `/api/pagamento/orcamento/{orcamentoId}` | Web | Pagamentos de um orçamento (histórico de tentativas) |
| `GET` | `/api/pagamento/ordem-servico/{ordemServicoId}` | Web | Pagamentos de uma OS |
| `POST` | `/api/pagamento/{id}/sincronizar` | Web / operação | Força `GET` no Mercado Pago e reaplica o status |
| `POST` | `/api/webhooks/mercadopago` | Mercado Pago | Notificação de mudança. `AllowAnonymous` |

Respostas seguem o mesmo envelope `ResponseDto` já usado no orçamento (`hasErrors`,
`getErrors`, `getResponse`).

### 2.1 `POST /api/pagamento`

```json
{
  "requisicaoId": "3f6a1c2e-9b7d-4a11-8f2e-5c0d7b9a1e44",
  "orcamentoId": 1,
  "token": "ff8080814c11e237014c1ff593b57b4d",
  "paymentMethodId": "master",
  "issuerId": 310,
  "installments": 1,
  "descricao": "Orcamento 000001 - OS 12",
  "payer": {
    "email": "test_user_123456@testuser.com",
    "identification": { "type": "CPF", "number": "12345678909" }
  }
}
```

| Campo | Obrigatório | Observação |
|---|---|---|
| `requisicaoId` | não | GUID gerado pelo Web. Reenvio com o mesmo valor **não cria segunda cobrança**. Ausente, o serviço gera |
| `orcamentoId` | sim | Orçamento a ser pago. Precisa existir neste banco |
| `token` | sim | Token do cartão gerado no Web. Uso único |
| `paymentMethodId` | sim | `master`, `visa`, `amex`, `elo`… vem do próprio SDK no Web |
| `issuerId` | não | Emissor, quando o SDK devolver |
| `installments` | sim | Parcelas. `1` para à vista |
| `descricao` | não | Vai como `description` no Mercado Pago |
| `payer.email` | sim | Precisa ser **diferente** do e-mail da conta Mercado Pago do vendedor |
| `payer.identification` | sim em teste | É o campo que força o resultado nos testes (seção 5) |

> **O valor não vem na requisição — de propósito.** O `transaction_amount` é lido do
> `Orcamento.ValorTotalGeral` no servidor. Se o Web mandasse o valor, um cliente poderia
> pagar R$ 0,01 por um orçamento de R$ 450. O Web não precisa, e não deve, enviar valor.

**Resposta `201`:**

```json
{
  "hasErrors": false,
  "getErrors": [],
  "getResponse": {
    "id": 7,
    "orcamentoId": 1,
    "ordemServicoId": 12,
    "valor": 450.00,
    "status": 4,
    "statusDescricao": "Aprovado",
    "statusDetalhe": "accredited",
    "provedor": "MercadoPago",
    "idExterno": "1234567890",
    "parcelas": 1,
    "criadoEm": "2026-11-06T14:40:02.112Z",
    "atualizadoEm": "2026-11-06T14:40:03.880Z"
  }
}
```

| Código | Quando |
|---|---|
| `201` | Pagamento criado — inclusive quando o Mercado Pago recusa. Recusa é **resultado**, não erro de API |
| `200` | `requisicaoId` repetido: devolve o pagamento já criado, sem cobrar de novo |
| `400` | Payload inválido, orçamento com valor zero, ou token recusado na origem |
| `404` | `orcamentoId` não existe |
| `409` | Já existe pagamento aprovado para esse orçamento |
| `502` | Mercado Pago indisponível ou respondeu erro não tratável |

### 2.2 `POST /api/webhooks/mercadopago`

Corpo enviado pelo Mercado Pago:

```json
{
  "id": 12345,
  "live_mode": false,
  "type": "payment",
  "action": "payment.updated",
  "date_created": "2026-11-06T10:04:58.396-04:00",
  "user_id": 44444,
  "api_version": "v1",
  "data": { "id": "1234567890" }
}
```

Regras de processamento:

1. **Validar `x-signature`** antes de qualquer coisa. HMAC sobre um manifest montado com o
   `data.id`, o header `x-request-id` e o `ts` da própria assinatura, usando o secret do
   webhook. Assinatura inválida → `401`, sem tocar no banco.
2. **Nunca confiar no corpo.** A notificação só diz *qual* pagamento mudou. O status real
   vem de `GET /v1/payments/{data.id}`.
3. **Responder `200` rápido.** O Mercado Pago espera confirmação em **22 segundos** e, sem
   ela, reenvia a cada **15 minutos** (a partir da terceira tentativa o intervalo aumenta).
4. **Responder `200` também quando a notificação é descartada** por idempotência ou por
   ser de um pagamento desconhecido. `4xx`/`5xx` aqui só faz o Mercado Pago reenviar para
   sempre.
5. Ignorar `type` diferente de `payment` — respondendo `200`.

### 2.3 `POST /api/pagamento/{id}/sincronizar`

Faz o que o webhook faria, sob demanda. Existe por três motivos concretos:

- **`localhost` não recebe webhook.** Sem URL pública (ngrok ou similar), este endpoint é a
  única forma de fechar o ciclo em desenvolvimento.
- Reprocessar notificação perdida.
- Operação: conferir divergência entre o nosso status e o do Mercado Pago.

Resposta `200` com o pagamento atualizado, ou `204` se nada mudou.

## 3. Modelo

### `Pagamento` (agregado)

| Campo | Observação |
|---|---|
| `Id` | PK local |
| `RequisicaoId` | GUID da tentativa. **Índice único** — é a idempotência de entrada |
| `OrcamentoId` | Orçamento pago. Sem FK para OS: ela vive em outro serviço |
| `OrdemServicoId` | Copiado do orçamento, para consulta direta |
| `Valor` | **Congelado** no momento da criação, lido do orçamento |
| `Status` | `StatusPagamento` (abaixo) |
| `StatusDetalhe` | `status_detail` do Mercado Pago, cru, para diagnóstico |
| `Provedor` | `MercadoPago`. Campo existe para não amarrar o modelo a um provedor |
| `IdExterno` | `id` do pagamento no Mercado Pago |
| `Parcelas`, `MetodoPagamento` | Como foi pago |
| `ModoTeste` | Espelha o `live_mode` da notificação. Separa teste de produção no mesmo banco |
| `UltimaAtualizacaoProvedor` | `date_last_updated` do Mercado Pago. **É o relógio da ordenação** |
| `MotivoCancelamento` | Preenchido quando o cancelamento é nosso, não do provedor. Ex.: `ValorDoOrcamentoAlterado` |
| `CriadoEm`, `AtualizadoEm` | Auditoria local |

O valor congelado segue o mesmo padrão do `OrdemServicoSnapshot` que já existe no
orçamento, e pelo mesmo motivo: o documento tem de retratar o que foi acordado, não o que
os dados dizem hoje.

### `StatusPagamento`

Espelha 1:1 os status do Mercado Pago, para não perder informação em tradução:

| Valor | Nome | `status` no Mercado Pago |
|---|---|---|
| `1` | `Pendente` | `pending` |
| `2` | `EmProcessamento` | `in_process` |
| `3` | `Autorizado` | `authorized` |
| `4` | `Aprovado` | `approved` |
| `5` | `Recusado` | `rejected` |
| `6` | `Cancelado` | `cancelled` |
| `7` | `Devolvido` | `refunded` |
| `8` | `EmDisputa` | `in_mediation` |
| `9` | `Estornado` | `charged_back` |

**A transição é máquina de estados, não sobrescrita.** Notificações chegam fora de ordem —
`pending` pode chegar depois de `approved`. Duas guardas:

1. Descarta a notificação cujo `date_last_updated` seja **anterior** ao
   `UltimaAtualizacaoProvedor` já gravado. É o mesmo raciocínio do `OcorridoEm` no evento
   de itens da OS ([ADR-002](adr/ADR-002-contrato-de-eventos.md)), com outro relógio.
2. `Recusado` e `Cancelado` são terminais. De `Aprovado` só se sai para `Devolvido`,
   `EmDisputa` ou `Estornado`.

### Recálculo do orçamento invalida pagamento pendente

**Decidido.** Quando um recálculo altera o `ValorTotalGeral` de um orçamento que tem
pagamento em `Pendente` ou `EmProcessamento`, esse pagamento é **cancelado** e o cliente
precisa pagar de novo, com o valor novo.

| Situação | O que acontece |
|---|---|
| Recálculo **não** muda o `ValorTotalGeral` | Nada. O pagamento pendente segue valendo |
| **Ajuste manual** muda o valor, pagamento em aberto | Mesmo tratamento do recálculo: ajuste manual muda o valor tanto quanto um evento de itens |
| Recálculo muda o valor, pagamento em `Pendente`/`EmProcessamento` | Pagamento vai para `Cancelado`, com `MotivoCancelamento = "ValorDoOrcamentoAlterado"`. Publica `pagamento.cancelado` |
| Recálculo muda o valor, pagamento `Aprovado` | **Não se toca no pagamento.** Fica divergência registrada, resolvida por estorno ou cobrança complementar — decisão humana |

O cancelamento é local: o pagamento não foi capturado, então não há estorno a pedir ao
Mercado Pago. Se o cliente concluir o pagamento cancelado no meio do caminho, o webhook
traz `Aprovado` e cai no terceiro caso — divergência registrada, não sobrescrita.

> Isso só é simples porque orçamento e pagamento estão no **mesmo serviço e no mesmo
> banco**: o cancelamento roda na mesma requisição do recálculo, com o mesmo `DbContext`, e
> uma falha é local e repetível. Não há transação única envolvendo os dois — são dois
> `SaveChanges` —, mas também não há saga, rede no meio nem estado parcial distribuído, que
> é o que a separação em dois serviços custaria. É o argumento mais concreto a favor da
> decisão de mantê-los juntos.

## 4. Eventos publicados

No tópico `orcamento-eventos` que já existe, com o `MessageAttribute` `tipoEvento`:

| Evento | Quando |
|---|---|
| `pagamento.criado` | Pagamento registrado, qualquer status inicial |
| `pagamento.aprovado` | Transição para `Aprovado` |
| `pagamento.recusado` | Transição para `Recusado` |
| `pagamento.cancelado` | Cancelamento, inclusive o por valor do orçamento alterado |
| `pagamento.estornado` | `Devolvido`, `EmDisputa` ou `Estornado` |

`pagamento.aprovado` é o gancho para quem for dono da Ordem de Serviço mover o status —
hoje o monólito, amanhã o serviço de OS que outro time está extraindo. Quem consome e como
a transição acontece é decisão de alinhamento entre times, registrada em
[alinhamento-e-discovery.md](alinhamento-e-discovery.md).

Publicação *best-effort*, como já é no orçamento: o pagamento está gravado, falha no SNS
não desfaz.

## 5. Modo de teste

Credenciais de teste são **separadas** das de produção, e vêm no painel do desenvolvedor em
*Detalhes da aplicação > Credenciais*. Com elas, nada é cobrado de verdade e as notificações
chegam com `live_mode: false`.

### Contas de teste: não são necessárias aqui

A página de compra de teste do Checkout API pede exatamente três coisas — credenciais de
teste da aplicação, um cartão de teste, e um e-mail de pagador **diferente do e-mail da sua
conta** Mercado Pago. **Conta de teste não é pré-requisito**, e as credenciais de teste saem
da própria conta de produção.

Faz sentido criar conta de teste quando o fluxo exigir que alguém **faça login** no Mercado
Pago — Checkout Pro, carteira, saldo em conta — ou para inspecionar o lado do vendedor sem
usar a conta real. Nenhum dos dois entra neste serviço: o pagador é identificado só por
e-mail e CPF, e o cartão é tokenizado no front-end.

Se um dia forem criadas: são duas, do mesmo país (vendedor e comprador), uma por aplicação
já vem criada, e o limite é 15.

### Cartões de teste

| Tipo | Bandeira | Número | CVV | Validade |
|---|---|---|---|---|
| Crédito | Mastercard | `5480 8328 0103 3311` | `123` | `11/30` |
| Crédito | Visa | `4235 6477 2802 5682` | `123` | `11/30` |
| Crédito | American Express | `3753 651535 56885` | `1234` | `11/30` |
| Débito | Elo | `5067 7667 8388 8311` | `123` | `11/30` |

### Forçar o resultado

O resultado **não** depende do cartão: depende do **nome do titular**. Qualquer cartão de
teste acima, variando o nome:

| Nome do titular | Resultado |
|---|---|
| `APRO` | Aprovado |
| `OTHE` | Recusado por erro geral |
| `CONT` | Pendente |
| `CALL` | Recusado — ligar para autorizar |
| `FUND` | Recusado por saldo insuficiente |
| `SECU` | Recusado por código de segurança inválido |
| `EXPI` | Recusado por problema na data de validade |
| `FORM` | Recusado por erro no formulário |
| `DUPL` | Pagamento duplicado |
| `LOCK` | Cartão desabilitado |
| `INST` | Parcelas inválidas |

Para `APRO` e `OTHE`, a documentação usa o CPF `12345678909`.

Isso dá um plano de teste direto para o microsserviço: `APRO` cobre o caminho felizardo,
`CONT` é o único que exercita de verdade o webhook e a máquina de estados (nasce `Pendente`
e muda depois), e `FUND`/`SECU`/`CALL` cobrem recusa com `status_detail` diferente.

### Checklist de teste do microsserviço

- [ ] `APRO` → `201` com `status: 4`
- [ ] `FUND` → `201` com `status: 5` e `statusDetalhe` preenchido (recusa não é erro de API)
- [ ] `CONT` → `201` com `status: 1`, e depois webhook levando a `Aprovado`
- [ ] Mesmo `requisicaoId` duas vezes → `200` e um único pagamento no banco
- [ ] Webhook com `x-signature` inválida → `401`, nada gravado
- [ ] Webhook de `data.id` desconhecido → `200` (não reenviar para sempre)
- [ ] Webhook com `date_last_updated` antigo → `200` e status **não** regride
- [ ] Sem URL pública: `POST /api/pagamento/{id}/sincronizar` fecha o ciclo

## 6. Configuração

| Chave | Variável no `.env` | Uso |
|---|---|---|
| `Pagamentos:Habilitado` | `PAGAMENTOS_HABILITADO` | `false` usa gateway fake, sem tocar no Mercado Pago. Padrão em dev e nos testes |
| `Pagamentos:MercadoPago:AccessToken` | `MERCADOPAGO_ACCESS_TOKEN` | Access token **de teste**. Nunca em `appsettings` |
| `Pagamentos:MercadoPago:WebhookSecret` | `MERCADOPAGO_WEBHOOK_SECRET` | Secret da validação de `x-signature`. Vazio = aceita sem validar e avisa no log |
| `Pagamentos:MercadoPago:NotificationUrl` | `MERCADOPAGO_NOTIFICATION_URL` | URL pública do webhook, registrada no painel |
| `Pagamentos:MercadoPago:BaseUrl` | — | Padrão `https://api.mercadopago.com` |

O `docker-compose.yml` mapeia cada variável do `.env` para a chave aninhada
(`Pagamentos__MercadoPago__AccessToken`, com duplo underscore). Os defaults mantêm o modo
fake, então **subir o compose sem `.env` funciona e não fala com o Mercado Pago** —
alternar para o provedor real é editar o `.env`, sem tocar em código.

Para `dotnet run` local, o token vai em User Secrets em vez do `.env`:

```bash
dotnet user-secrets set "Pagamentos:MercadoPago:AccessToken" "TEST-..." \
  --project src/OrcamentoPagamento.API
```

Em produção, os segredos seguem o caminho que já existe para o `JWT_SECRET_KEY`: SSM
Parameter Store (`SecureString`) → Secret do Kubernetes.

## 7. Como isso se encaixa na arquitetura atual

O desenho repete, de propósito, o que já está implementado para a mensageria:

| Pagamento | Equivalente já existente |
|---|---|
| `IGatewayPagamento` (porta no `Domain`) | `IPublicadorEventos` |
| `MercadoPagoGateway` (único arquivo que conhece o Mercado Pago) | `SnsPublicadorEventos` |
| `GatewayPagamentoFake` | `PublicadorEventosLog` |
| `Pagamentos:Habilitado` | `Messaging:Habilitado` |

Consequência prática: o fluxo inteiro, inclusive webhook e máquina de estados, roda em
teste automatizado sem o Mercado Pago. CI não pode depender de sandbox de terceiro.

Webhook público num serviço hoje 100% autenticado tem precedente no projeto: o link de
aprovação de OS por e-mail, no monólito, também é rota aberta com validação própria.

## 8. Decidido e pendente

### Decidido

| # | Decisão | Onde está detalhada |
|---|---|---|
| 1 | Pagamento fica **no mesmo serviço** do orçamento, como módulo separado | [ADR-001](adr/ADR-001-extracao-do-orcamento-como-microsservico.md) e seção 7 |
| 2 | **Checkout API**, com tokenização no Web. O cartão não passa por este backend | Seção 1 |
| 3 | Recálculo que altera o valor **cancela** pagamento pendente. `Aprovado` nunca é tocado | Seção 3 |
| 4 | Apenas credenciais e contas de **teste** neste primeiro momento | Seção 5 |
| 5 | O **valor vem do orçamento**, nunca da requisição do Web | Seção 2.1 |

### Pendente — depende de outro time

**Em que ponto do fluxo da OS o pagamento acontece.** A definição inicial é: a OS em
`AguardandoAprovacao` passa para `EmExecucao` **após o pagamento**.

```text
Recebida → EmDiagnostico → AguardandoAprovacao ──[pagamento aprovado]──▶ EmExecucao
                                                                            │
                                                          Finalizada ◀──────┘
                                                               │
                                                           Entregue
```

Isso está **pendente de confirmação**, e deliberadamente não implementado, por dois motivos:

1. A Ordem de Serviço está sendo externalizada para outro serviço, por outro time. Quem é
   dono dessa transição muda junto.
2. A regra pode mudar nessa externalização.

O que **este** serviço faz enquanto isso: publica `pagamento.aprovado` e para aí. Não
conhece status de OS, não tenta mover nada. Quem reage ao evento é decisão do outro lado.

Duas consequências dessa definição que precisam de resposta antes de valer como regra — e
que estão rastreadas em [alinhamento-e-discovery.md](alinhamento-e-discovery.md):

- **Já existem dois caminhos para `AguardandoAprovacao → EmExecucao` sem pagamento:** o link
  de aprovação por e-mail (`GET /api/AprovacaoOrdemServico/{token}/aprovar`) e o
  `PATCH /api/ordemservico/{id}/aprovar`. Pagamento passa a ser **obrigatório** para
  aprovar, ou é um terceiro caminho que coexiste?
- **Pagar em `AguardandoAprovacao` é pagar antes da execução.** Se itens forem adicionados
  durante `EmExecucao`, o orçamento é recalculado e o valor pago já não cobre o total. Cai
  na divergência da seção 3, que hoje é "decisão humana". Com pagamento antecipado, isso
  deixa de ser exceção e passa a ser rotina — provavelmente precisa de cobrança
  complementar como fluxo de verdade.

### Pendente — nosso, mas não bloqueia

| # | Questão | Situação |
|---|---|---|
| 6 | **Pagamento parcial.** O modelo permite N pagamentos por orçamento (histórico de tentativas), com "um aprovado por orçamento" proposto como `409` | Confirmar se vale pagamento em partes. A cobrança complementar acima pode forçar isso |
| 7 | **Pix.** O modelo já suporta (`MetodoPagamento`, status `Pendente`), mas é `POST /v1/payments` com `payment_method_id: "pix"` e sem token de cartão | Fluxo diferente no Web; fora do escopo deste primeiro teste |
| 8 | **Migrar para a Orders API.** É a recomendada para integração nova | Decidido ficar na Payments por ora. Detalhes, custo e gatilho em [§ 10](#10-dívida-técnica-payments-api-vs-orders-api) |

Nada disso bloqueia a implementação. O módulo pode ser construído e testado exatamente como
está especificado: o pagamento é disparado explicitamente pelo Web, e o serviço publica o
resultado sem saber quem escuta.

## 9. Referências

| Fonte | Assunto |
|---|---|
| [Fazer compra de teste](https://www.mercadopago.com.br/developers/pt/docs/checkout-api-payments/integration-test/make-test-purchase) | Fluxo de teste com credenciais de teste |
| [Cartões de teste](https://www.mercadopago.com.br/developers/pt/docs/checkout-api-payments/additional-content/your-integrations/test/cards) | Cartões e nomes que forçam cada resultado |
| [Contas de teste](https://www.mercadopago.com.br/developers/pt/docs/checkout-api-payments/additional-content/your-integrations/test/accounts) | Quando contas de teste sao uteis (nao e o caso deste fluxo) |
| [Integração via Métodos Core](https://www.mercadopago.com.br/developers/pt/docs/checkout-api-payments/integration-configuration/card/integrate-via-core-methods) | `POST /v1/payments` e `createCardToken` |
| [Webhooks](https://www.mercadopago.com.br/developers/pt/docs/your-integrations/notifications/webhooks) | Corpo da notificação, 22s, retry e `x-signature` |
| [Coleção Postman](https://documenter.getpostman.com/view/15366798/2sAXjKasp4) | Requisições da API do Mercado Pago |
| [Visão geral do Checkout Transparente](https://www.mercadopago.com.br/developers/pt/docs/checkout-api-payments/overview) | Onde está a recomendação de usar Orders em integração nova |
| [Migrar Payments → Orders](https://www.mercadopago.com.br/developers/pt/docs/checkout-api-orders/resources/migrate-payments-to-orders) | Diferenças de contrato e bloqueios da migração |
| [ADR-002](adr/ADR-002-contrato-de-eventos.md) | O padrão de idempotência que a máquina de estados reaproveita |

## 10. Dívida técnica: Payments API vs Orders API

O Mercado Pago tem duas variantes do Checkout Transparente, e a página de visão geral é
explícita sobre a preferência:

> *"Se você está começando uma nova integração, recomendamos utilizar a API de Orders."*

Sobre a Payments, a mesma página: *"essa API não receberá novas funcionalidades, apenas
correções de segurança e estabilidade."* A página de migração reforça que o Mercado Pago
**não desativou** a Payments — ela não está depreciada, está em manutenção.

**Este serviço implementa a Payments API** (`POST /v1/payments`). Registro honesto de como
chegamos aqui: foi implementada sem que a existência da variante Orders fosse considerada.
Ao descobrir, a decisão consciente foi **manter a Payments por ora**, porque o fluxo já está
implementado e verificado ponta a ponta, e a API segue funcional e sem data de desligamento.

### O que custa migrar

A porta `IGatewayPagamento` **não muda**. É o retorno concreto de ter o provedor atrás de um
adapter:

| Onde | Mudança |
|---|---|
| `MercadoPagoGateway.CriarAsync` | `POST /v1/orders`. Corpo reestruturado: `type: "online"`, `processing_mode`, `total_amount` como **string**, e `transaction_amount`/`token`/`installments`/`payment_method_id` migram para `transactions.payments[]` com os campos de método aninhados em `payment_method` |
| idem, captura | O booleano `capture` vira `capture_mode` na raiz (`manual`, `automatic`, `automatic_async`) |
| `MercadoPagoGateway.ObterAsync` | `GET /v1/orders/{id}` |
| `MapearStatus` | Atenção: Orders usa `canceled` (um L), Payments usa `cancelled` |
| `WebhookMercadoPagoController` | Hoje filtra `type == "payment"`; provavelmente passa a ser o recurso `order` |
| `MERCADOPAGO_NOTIFICATION_URL` | Na Orders a `notification_url` sai do corpo e é configurada no painel |

**Não mudam:** `Domain`, `Application`, controllers de pagamento, máquina de estados,
`GatewayPagamentoFake` e os 102 testes — exceto o mapa de status e o filtro do webhook.

### Bloqueios declarados pela documentação

Nenhum dos dois nos afeta hoje, mas valem conferência antes de migrar:

- **Débito.** A Orders cobre apenas Elo e débito virtual Caixa; a Payments aceita débito de
  todas as bandeiras. A doc orienta manter na Payments o fluxo que depender de outras.
- **Marketplace / split 1:1.** A Orders não tem equivalente documentado de
  `application_fee`. A doc trata isso como **bloqueio**. Não usamos split.

### Gatilho para revisitar

Migrar deixa de ser opcional se qualquer um destes acontecer:

1. Precisarmos de **3DS 2.0**, captura em duas etapas, ou múltiplas transações por cobrança.
2. O Mercado Pago anunciar data de desligamento da Payments.
3. O módulo de pagamento precisar de alguma funcionalidade nova do provedor — por definição,
   elas só chegam na Orders.

Antes de implementar, ler a referência da Orders campo a campo. O que está nesta seção veio
da página de migração, não da referência completa.
