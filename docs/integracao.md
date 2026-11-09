# Integração — o que falta nos outros repositórios

Este microsserviço está completo e testado, mas só recebe eventos quando o produtor passar
a publicá-los. Abaixo, o que precisa ser feito em cada repositório. Nada disso foi alterado
por aqui.

## 1. MechanicLtda (monólito) — publicar o evento

Onde: `src/MechanicLtda.Domain/Services/ItemOrdemServicoService.cs`. Hoje, nas três
chamadas de `_orcamentoService.CriarOuAtualizarAsync(...)` (linhas ~82, ~114 e ~141), o
orçamento é recalculado em processo. O passo é publicar
`ordem-servico.itens-alterados` no tópico SNS **nos mesmos três pontos**, montando o
payload descrito em [ADR-002](adr/ADR-002-contrato-de-eventos.md).

A categoria de cada item vem de:

| Item da OS | `categoria` no evento |
|---|---|
| `EstoqueId` preenchido e `Estoque.Tipo == TipoEstoque.Peca` | `1` (Peca) |
| `EstoqueId` preenchido e `Estoque.Tipo == TipoEstoque.Insumo` | `2` (Insumo) |
| `ServicoOficinaId` preenchido, ou só `DescricaoServico` | `3` (Servico) |

Recomendações:

- Publicar **depois** do `SaveChanges` dos itens, para não emitir evento de uma transação
  que falhou.
- Propagar o `X-Correlation-Id` da requisição no campo `correlationId`.
- `ocorridoEm` em UTC; é o campo que resolve evento fora de ordem.
- Falha ao publicar deve ser *best-effort* (log + evento `FalhaIntegracao`), como já é o
  envio de e-mail — e o reprocessamento fica por `POST /api/orcamento/recalcular`.

Estratégia de transição sugerida, para não ter duas fontes de verdade do orçamento:

1. Publicar o evento **mantendo** o cálculo em processo (duplo cálculo; os bancos ficam
   iguais e dá para comparar).
2. Apontar o roteamento do gateway de `/api/orcamento/*` para o microsserviço.
3. Remover `OrcamentoService`, `OrcamentoPdfAppService`, `OrcamentoRepository`, a entidade
   `Orcamento` e a tabela `Orcamentos` do monólito.

## 2. MechanicLtda.Web — trocar a chamada em processo por HTTP

`src/MechanicLtda.Web/Controllers/OrcamentosController.cs` usa `IOrcamentoAppService`
direto. Precisa virar um `HttpClient` tipado apontando para o gateway. O envelope de
resposta é o mesmo `ResponseDto`, então as views e os `FlashErrors`/`AddErrors` continuam
valendo. Detalhes e trade-offs em [ADR-003](adr/ADR-003-consumo-pelo-web-via-api-gateway.md).

Rotas equivalentes:

| Web hoje (em processo) | Microsserviço (HTTP) |
|---|---|
| `ObterPorIdAsync(id)` | `GET /api/orcamento/{id}` |
| `ObterPorOrdemServicoIdAsync(osId)` | `GET /api/orcamento/ordem-servico/{osId}` |
| `AtualizarManualAsync(id, dto)` | `PUT /api/orcamento/{id}` |
| `ExportarPdfAsync(id)` | `GET /api/orcamento/{id}/pdf` |
| `RemoverAsync(id)` | `DELETE /api/orcamento/{id}` |

Atenção a uma diferença de contrato: `OrcamentoUpdateDto` ganhou
`ValorTotalServicos`, e o total geral passou a ser a soma das três categorias. A tela de
ajuste manual precisa do campo novo.

## 3. InfraKubernete — ECR, SNS/SQS e manifestos

- **ECR**: repositório `mechanicltda-orcamento` (`infra/ecr.tf`). É o nome usado em
  `ECR_REPOSITORY` no workflow deste repo.
- **SNS/SQS** (novo arquivo, ex. `infra/messaging.tf`):

  | Recurso | Nome | Observação |
  |---|---|---|
  | Tópico SNS | `ordem-servico-eventos` | publicado pelo monólito |
  | Fila SQS | `ordem-servico-itens-alterados` | assinada no tópico, `RawMessageDelivery = true`, filter policy por `tipoEvento` |
  | Fila SQS | `ordem-servico-itens-alterados-dlq` | redrive com `maxReceiveCount = 5` |
  | Tópico SNS | `orcamento-eventos` | publicado por este serviço |

  O `scripts/localstack/01-criar-topico-e-fila.sh` deste repositório é a referência exata
  dessa topologia — o Terraform deve reproduzi-la.

- **IAM**: a Service Account / role dos pods do orçamento precisa de `sqs:ReceiveMessage`,
  `sqs:DeleteMessage`, `sqs:GetQueueAttributes` na fila e `sns:Publish` no tópico
  `orcamento-eventos`. O monólito precisa de `sns:Publish` em `ordem-servico-eventos`.
- **Manifestos** (`k8s/base/`): Deployment, Service, HPA e Secret do orçamento. O Secret
  precisa de `ConnectionStrings__DefaultConnection`, `JWT_SECRET_KEY`,
  `Messaging__TopicoEventosArn` e `Messaging__FilaItensAlteradosUrl`.
  Probes: `GET /health`.
- **API Gateway**: rota `/api/orcamento/{proxy+}` apontando para o Service do orçamento.
  Precisa vir **antes** da rota curinga do monólito, que é mais genérica.

## 4. InfraSGBD — banco do serviço

Criar `MechanicOrcamentoDb` na instância RDS existente e publicar a connection string no
SSM Parameter Store (`SecureString`), no mesmo padrão do banco do monólito. Banco separado,
mesma instância: é o que a [ADR-001](adr/ADR-001-extracao-do-orcamento-como-microsservico.md)
decidiu. As migrations são aplicadas pelo próprio serviço na subida.
