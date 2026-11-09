# Alinhamento entre times e discovery

- **Documento vivo.** Atualizar conforme as decisões caem, não ao final.
- **Última atualização:** 2026-11-06
- **Dono deste documento:** time do microsserviço de Orçamento e Pagamento

Três frentes estão sendo desenvolvidas em paralelo, e nenhuma delas pode esperar as outras.
Este documento existe para que isso não vire integração-surpresa: registra o que cada time
assume do outro, o que já foi acordado, e o que ainda vai doer se ninguém decidir.

Se você veio parar aqui procurando um contrato específico, ele está em outro lugar — este
documento aponta, não duplica.

## 1. Quem está fazendo o quê, agora

| Frente | Time | Estado | Contrato |
|---|---|---|---|
| Microsserviço de Orçamento | nosso | **implementado e testado** | [README](../README.md), [implementacao.md](implementacao.md) |
| Módulo de Pagamento (Mercado Pago) | nosso | **implementado e testado** | [pagamento.md](pagamento.md) |
| Chamada de pagamento no Web | outro time | em desenvolvimento | [pagamento.md § 2](pagamento.md) |
| Externalização da Ordem de Serviço | outro time | em desenvolvimento | ainda não definido |
| Publicação do evento de itens no monólito | ninguém ainda | **não iniciado** | [ADR-002](adr/ADR-002-contrato-de-eventos.md) |

> **Estado real hoje:** ninguém publica `ordem-servico.itens-alterados`. O orçamento só é
> calculado por `POST /api/orcamento/recalcular`. O serviço está pronto, mas ocioso até
> alguém produzir o evento — seja o monólito, seja o novo serviço de OS.

## 2. Contratos em jogo

### 2.1 Ordem de Serviço → Orçamento (nós consumimos)

| Item | Situação |
|---|---|
| Evento | `ordem-servico.itens-alterados`, tópico `ordem-servico-eventos` |
| Contrato | **definido e congelado** em [ADR-002](adr/ADR-002-contrato-de-eventos.md) |
| Produtor hoje | monólito MechanicLtda — **ainda não implementado** |
| Produtor amanhã | serviço de OS, do outro time |

O contrato foi escrito sem depender de quem publica: payload auto-contido, com os itens já
classificados. Isso não foi sorte — foi para que a troca de produtor não exigisse
renegociação. **O contrato sobrevive à externalização da OS.**

O que o produtor precisa assumir, em qualquer um dos dois cenários:

- Publicar **depois** do commit dos itens, nunca antes.
- Mandar o **estado atual completo** dos itens, não um delta.
- **Classificar cada item** em `Peca`, `Insumo` ou `Servico` (ver [§ 3.2](#32-quem-classifica-o-item)).
- `ocorridoEm` em UTC — é o relógio que o consumidor usa para descartar evento atrasado.
- Propagar o `X-Correlation-Id` no campo `correlationId`.

### 2.2 Orçamento e Pagamento → Ordem de Serviço (nós publicamos)

| Evento | Quem deveria consumir | Decidido? |
|---|---|---|
| `orcamento.calculado` | ninguém hoje | — |
| `pagamento.aprovado` | quem for dono da OS, para mover o status | **não** |
| `pagamento.recusado` / `cancelado` | idem | **não** |

Tudo no tópico `orcamento-eventos`, com `MessageAttribute` `tipoEvento` — dá para assinar
só o que interessa, via filter policy, sem receber o resto.

### 2.3 Web → Pagamento (eles chamam, nós servimos)

Contrato em [pagamento.md § 2](pagamento.md). Os quatro pontos que mais geram retrabalho se
não forem lidos:

1. **O `valor` não vai no payload.** Vem do orçamento, no servidor.
2. **Recusa é `201`, não `400`.** `FUND`, `SECU`, `CALL` são resultado de negócio.
3. **O Web gera o `requisicaoId`** (GUID). É o que impede cobrança dupla em retry.
4. **O cartão nunca chega ao nosso backend.** O Web tokeniza e manda só o `token`.

## 3. Discovery — perguntas abertas

Ordenadas por quanto custa descobrir tarde.

### 3.1 O id da Ordem de Serviço sobrevive à externalização?

**Impacto: alto. Pergunta mais importante deste documento.**

`Orcamento.OrdemServicoId` é a única amarra entre o orçamento e a OS. Se o serviço novo de
OS nascer com banco próprio e **identidade nova** (ids recomeçando, ou GUID em vez de int),
todo orçamento já gravado passa a apontar para um id que não existe mais.

Precisamos saber:

- Os ids de OS serão **preservados** na migração, ou reatribuídos?
- O tipo continua `int`, ou vira GUID / id composto?
- Se mudar, quem faz o remapeamento — e temos janela para isso?

Decidir isso **antes** de haver volume de orçamentos em produção é muito mais barato.

### 3.2 Quem classifica o item?

**Impacto: alto.**

O contrato exige `categoria` (`Peca`, `Insumo`, `Servico`) já resolvida pelo produtor,
porque hoje isso depende de `Estoque.Tipo` — dado do contexto de Catálogo.

- Se **Estoque também for externalizado**, o serviço de OS deixa de ter esse dado local e
  passa a precisar consultar o Catálogo só para publicar o evento. Isso é aceitável?
- Alternativa: o produtor manda `estoqueId` cru e o orçamento resolve. **Não recomendamos** —
  recria exatamente o acoplamento que a extração eliminou, e faz o PDF de um orçamento
  antigo mudar quando o cadastro do estoque mudar.
- Terceira via: o Catálogo publica suas próprias mudanças e cada serviço mantém a projeção
  que precisa. Mais limpo, mais trabalho.

### 3.3 Pagamento é obrigatório para aprovar a OS?

**Impacto: alto. Bloqueia a regra de negócio do pagamento.**

A definição inicial é: `AguardandoAprovacao` → `EmExecucao` **após o pagamento**. Mas já
existem **dois caminhos** para essa mesma transição, hoje, sem pagamento nenhum:

| Caminho existente | Onde |
|---|---|
| Link de aprovação por e-mail, sem login | `GET /api/AprovacaoOrdemServico/{token}/aprovar` |
| Aprovação por funcionário | `PATCH /api/ordemservico/{id}/aprovar` |

Então: pagamento **substitui** esses caminhos, ou é um terceiro que coexiste? Se coexiste,
uma OS pode entrar em execução sem pagamento — e aí o pagamento vira cobrança posterior,
não pré-requisito. São duas regras de negócio bem diferentes.

### 3.4 Itens adicionados durante a execução, com pagamento já aprovado

**Impacto: alto. Consequência direta de pagar em `AguardandoAprovacao`.**

Pagando antes da execução, este cenário deixa de ser exceção e passa a ser rotina:

```text
Orcamento R$ 450  →  cliente paga R$ 450  →  OS entra em execucao
                                                      │
                          mecanico descobre que precisa trocar a correia
                                                      │
                                       itens alterados → orcamento R$ 680
                                                      │
                                    pagamento aprovado de R$ 450 nao cobre
```

Hoje a especificação trata isso como "divergência, decisão humana"
([pagamento.md § 3](pagamento.md)) — o que serve para exceção, não para rotina. Precisamos
saber se vai existir **cobrança complementar** como fluxo de verdade. Se sim, muda o modelo:
deixa de ser "um pagamento aprovado por orçamento" e passa a ser N pagamentos somando o
total, o que reabre a questão 6 da seção 8 da especificação.

### 3.5 Quem move o status da OS depois do pagamento aprovado?

**Impacto: médio. Decisão de desenho, com dois caminhos defensáveis.**

| Opção | Como funciona | Risco |
|---|---|---|
| **Por evento** (recomendamos) | Publicamos `pagamento.aprovado`; o dono da OS assina e move o status | Consistência eventual: a tela confirma o pagamento antes da OS mudar de status |
| Orquestrado pelo Web | Web paga, recebe `Aprovado`, e então chama a OS para aprovar | Se a segunda chamada falhar, o cliente **pagou** e a OS ficou parada. Dinheiro de um lado, estado do outro |

Nossa posição: **por evento**. Dinheiro já capturado não deve depender de uma segunda
chamada síncrona dar certo. A tela pode dizer "pagamento aprovado, liberando a ordem" sem
mentir.

### 3.6 Credenciais do Mercado Pago — mesma aplicação nos dois lados

**Impacto: médio, mas é um dia perdido se escapar.**

O Web tokeniza o cartão com a **public key**; nós criamos o pagamento com o **access
token**. As duas credenciais precisam ser da **mesma aplicação** do Mercado Pago — token
gerado pela public key de uma aplicação não é aceito pelo access token de outra.

Itens de alinhamento:

- [ ] Confirmar que Web e microsserviço usam as credenciais de **teste** da **mesma** aplicação.
- [ ] Definir quem é dono da aplicação no painel e quem distribui as credenciais.
- [ ] Criar as duas contas de teste (vendedor e comprador) e combinar quem usa qual. Limite: 15.
- [ ] O e-mail do `payer` tem de ser **diferente** do e-mail da conta vendedora, senão o
      pagamento falha por motivo que não parece ser esse.

### 3.7 `notification_url` e ambientes

**Impacto: médio.**

A URL de webhook é registrada no painel do Mercado Pago, por aplicação. Isso cria atrito
prático:

- Em desenvolvimento, `localhost` não recebe webhook. Precisa de túnel (ngrok ou similar), e
  a URL **muda a cada reinício** — alguém tem de manter o painel atualizado, ou pagar por
  um domínio estável.
- Com uma aplicação só, dev e produção disputam a mesma `notification_url`. Vale aplicação
  separada por ambiente?
- Nosso contorno enquanto isso: `POST /api/pagamento/{id}/sincronizar`, que faz o que o
  webhook faria, sob demanda. O time do Web consegue testar o ciclo completo com isso.

## 4. O que assumimos do outro lado

Se alguma destas premissas estiver errada, fale **agora** — todas são baratas de corrigir
hoje e caras depois.

| # | Premissa | Se estiver errada |
|---|---|---|
| 1 | O contrato da [ADR-002](adr/ADR-002-contrato-de-eventos.md) será honrado por quem publicar o evento, monólito ou serviço novo | Reescrita do consumidor e dos testes |
| 2 | Os nomes de tópico e fila (`ordem-servico-eventos`, `ordem-servico-itens-alterados`) valem para todos | Só configuração, barato |
| 3 | O `ordemServicoId` continua `int` e estável | Migração de dados + mudança de modelo. **Caro** |
| 4 | O produtor classifica o item antes de publicar | Reabre [§ 3.2](#32-quem-classifica-o-item), com impacto no desenho |
| 5 | O Web gera e reenvia o mesmo `requisicaoId` em retry | Risco de cobrança duplicada |
| 6 | Ninguém mais escreve em `MechanicOrcamentoDb` além deste serviço | Quebra o database-per-service da [ADR-001](adr/ADR-001-extracao-do-orcamento-como-microsservico.md) |
| 7 | O orçamento continua sendo responsabilidade **só** deste serviço depois da externalização da OS | Dois donos do mesmo dado |

## 5. Log de decisões

| Data | Decisão | Quem |
|---|---|---|
| 2026-11-06 | Orçamento extraído para serviço próprio, com banco próprio | nosso time |
| 2026-11-06 | Pagamento fica no **mesmo serviço** do orçamento, como módulo separado | nosso time |
| 2026-11-06 | Integração por **Checkout API**, com tokenização no Web | nosso time + Web |
| 2026-11-06 | Apenas credenciais e contas de **teste**; sem dinheiro real | nosso time |
| 2026-11-06 | Recálculo que altera o valor **cancela** pagamento pendente; `Aprovado` não é tocado | nosso time |
| 2026-11-06 | O valor da cobrança vem do orçamento, **nunca** da requisição do Web | nosso time |
| 2026-11-06 | Definição inicial: `AguardandoAprovacao` → `EmExecucao` após o pagamento — **registrada como pendente** até a externalização da OS se definir | Web / OS |

## 6. Próximo passo de cada frente

| Frente | Próximo passo | Depende de |
|---|---|---|
| Nosso — pagamento | Pronto. Falta exercitar contra o sandbox real do Mercado Pago | credenciais da mesma aplicação ([§ 3.6](#36-credenciais-do-mercado-pago--mesma-aplicação-nos-dois-lados)) |
| Nosso — orçamento | Nada. Pronto e ocioso | alguém publicar o evento |
| Web | Codar contra [pagamento.md § 2](pagamento.md) | credenciais da mesma aplicação ([§ 3.6](#36-credenciais-do-mercado-pago--mesma-aplicação-nos-dois-lados)) |
| OS | Decidir [§ 3.1](#31-o-id-da-ordem-de-serviço-sobrevive-à-externalização) e [§ 3.2](#32-quem-classifica-o-item) | — |
| Monólito | Publicar o evento, ou declarar que quem vai publicar é o serviço novo de OS | [§ 2.1](#21-ordem-de-serviço--orçamento-nós-consumimos) |
