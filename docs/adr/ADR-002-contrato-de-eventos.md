# ADR-002 — Contrato de eventos: payload auto-contido e idempotência pelo evento

- **Status:** aceito
- **Data:** 2026-11-06

## Contexto

O orçamento é recalculado a cada alteração nos itens da OS. Com o cálculo em outro serviço
e em outro banco, duas perguntas precisam de resposta explícita:

1. **De onde vêm os dados do cálculo?** O serviço precisa de quantidade, valor unitário e
   da classificação do item (peça, insumo ou serviço). No monólito, a classificação vem do
   `TipoEstoque` do item de estoque consumido — dado que mora no contexto de Catálogo.
2. **O que acontece com entrega duplicada e fora de ordem?** O SQS entrega *ao menos uma
   vez* e não garante ordem entre mensagens. Dois eventos da mesma OS podem chegar
   invertidos, e qualquer mensagem pode chegar duas vezes.

## Decisão

### 1. O evento é auto-contido

`ordem-servico.itens-alterados` carrega o **estado atual completo** dos itens da OS, já
classificados, mais os dados de OS/cliente/veículo usados no PDF:

```json
{
  "eventoId": "8f1d...-...",
  "tipoEvento": "ordem-servico.itens-alterados",
  "ocorridoEm": "2026-11-06T14:32:10.512Z",
  "correlationId": "a1b2c3d4e5f6",
  "ordemServicoId": 12,
  "ordemServico": {
    "descricaoProblema": "Revisao dos 20 mil km",
    "status": "EmDiagnostico",
    "clienteNome": "Carlos Oliveira",
    "clienteEmail": "carlos@email.com",
    "clienteTelefone": "11999990001",
    "veiculoMarca": "Toyota",
    "veiculoModelo": "Corolla",
    "veiculoPlaca": "ABC1D23",
    "veiculoAno": 2021
  },
  "itens": [
    { "itemId": 41, "descricao": "Pastilha de freio", "categoria": 1, "quantidade": 2, "valorUnitario": 100.00 },
    { "itemId": 42, "descricao": "Oleo 5W30",         "categoria": 2, "quantidade": 2, "valorUnitario": 50.00  },
    { "itemId": 43, "descricao": "Mao de obra",       "categoria": 3, "quantidade": 1, "valorUnitario": 150.00 }
  ]
}
```

`categoria`: `1 = Peca`, `2 = Insumo`, `3 = Servico`. **Quem publica resolve a
categoria** — é o lado que já tem o item de estoque e o serviço de oficina em mãos.

### 2. É o estado atual, não um delta

O consumidor **substitui** todos os itens do orçamento pelos do evento. Não há eventos de
"item adicionado"/"item removido" que precisariam ser aplicados na ordem certa.

### 3. Idempotência e ordem pelo próprio evento

O orçamento guarda `UltimoEventoId` e `UltimoEventoEm`. Um evento é descartado, sem erro,
quando:

- `eventoId` é igual ao último aplicado (reentrega), ou
- `ocorridoEm` é anterior ao `ocorridoEm` do último aplicado (evento atrasado).

O descarte responde `204 No Content` na rota de recálculo e apaga a mensagem da fila — não
é falha.

### 4. Tratamento de erro no consumo

| Situação | Ação |
|---|---|
| Processamento com sucesso ou descarte por idempotência | Apaga a mensagem |
| JSON inválido ou evento sem `ordemServicoId` | Apaga a mensagem e registra erro — reprocessar não conserta o payload |
| Falha de banco ou erro inesperado | **Não** apaga: a mensagem volta após o *visibility timeout* e, esgotado o `maxReceiveCount`, cai na DLQ |

### 5. Eventos publicados por este serviço

No tópico `orcamento-eventos`, com o `MessageAttribute` `tipoEvento` para permitir *filter
policy* por assinante: `orcamento.calculado`, `orcamento.ajustado` e `orcamento.removido`.
A publicação é *best-effort*: o orçamento já está gravado, então falha no SNS é registrada
e notificada, não desfaz o cálculo — mesma política que o monólito usa com o SMTP.

## Alternativas

| Alternativa | Por que não |
|---|---|
| Evento só com `ordemServicoId`, e o serviço busca o resto por HTTP | Volta a acoplar o orçamento à disponibilidade da Oficina e do Catálogo, e faz o PDF de um orçamento antigo mudar junto com os dados atuais |
| Eventos de delta (`item-adicionado`, `item-removido`) | Exigiria ordem garantida (SQS FIFO com `MessageGroupId` por OS) e reconciliação quando um evento se perdesse |
| Tabela de eventos processados (`EventoProcessado`) para idempotência | Mais uma tabela e mais uma escrita por mensagem; com estado atual e não delta, guardar o último evento no próprio orçamento resolve |
| Deduplicação do SQS FIFO | A janela é de 5 minutos; não cobre reentrega depois disso nem o evento atrasado |
| `ocorridoEm` apenas informativo, aceitando tudo | Um evento atrasado sobrescreveria um cálculo mais novo com dados velhos |

## Consequências

- O payload é maior do que seria um evento mínimo. Para a ordem de grandeza aqui (itens por
  OS), é irrelevante frente ao limite de 256 KB do SNS/SQS.
- O produtor fica responsável por classificar o item. Se ele errar a categoria, o total
  geral continua correto (soma dos itens), mas a quebra por categoria sai errada.
- Mudança de contrato precisa ser compatível: campo novo é opcional, e campo que mude de
  significado exige um `tipoEvento` novo (`...-alterados.v2`) com assinatura própria.
- A fila é assinada no tópico com `RawMessageDelivery = true`, mas o consumidor também
  aceita o envelope do SNS (campo `Message` como string) — assim trocar essa configuração
  não quebra o consumo.
