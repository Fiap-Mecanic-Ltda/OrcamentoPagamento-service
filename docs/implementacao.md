# O que foi implementado e de onde veio

- **Data:** 2026-11-06
- **Origem:** [MechanicLtda](https://github.com/Fiap-Mecanic-Ltda/MechanicLtda) — monólito
  modular .NET 9, de onde o contexto de orçamento foi espelhado

Este documento registra o inventário do que existe neste repositório e a procedência de
cada parte: o que foi portado do monólito como estava, o que foi adaptado na portagem, e o
que nasceu aqui porque microsserviço exige.

Serve para quem for mexer num arquivo e quiser saber se existe um equivalente no monólito a
consultar — e se ele ainda é equivalente.

## 1. Inventário

| Camada | Conteúdo |
|---|---|
| `Domain` | `Orcamento` (agregado raiz), `ItemOrcamento`, `OrdemServicoSnapshot`, `CategoriaItem`, `OrcamentoService`, `CalcularOrcamentoCommand`, contratos de repositório/serviço/publicador. **Pagamento:** `Pagamento` (agregado), `StatusPagamento`, `PagamentoService`, porta `IGatewayPagamento` |
| `Application` | `OrcamentoAppService`, `OrcamentoPdfAppService`, DTOs, `ResponseDto`, contrato do evento consumido, profile do AutoMapper, `JwtSecretProvider` |
| `Infrastructure` | `BancoOrcamentoContext`, Fluent API das 4 tabelas, `OrcamentoRepository`, `PagamentoRepository`, migrations, `SnsPublicadorEventos`, `PublicadorEventosLog`, `MercadoPagoGateway`, `GatewayPagamentoFake`, `ValidadorAssinaturaMercadoPago` |
| `API` | `OrcamentoController` (6 rotas), `PagamentoController` (5 rotas), `WebhookMercadoPagoController`, `SqsConsumidorItensAlterados`, extensões de DI/banco/JWT/Swagger/mensageria/pagamentos, `CorrelationIdMiddleware`, `Roles`, Dockerfile |
| `tests` | 70 testes de domínio + 32 de integração |

Fora do código: `docker-compose.yml` com SQL Server e LocalStack, script de bootstrap da
mensageria local, workflow de CI/CD, 3 ADRs e a coleção Postman.

> **Pagamento também está implementado.** O módulo do Mercado Pago foi construído conforme
> [pagamento.md](pagamento.md) e tem seção própria aqui (§ 4.1). Nada dele veio do
> monólito — é código novo.

## 2. Portado do monólito sem mudança de comportamento

Estes arquivos são equivalentes aos de lá. Se a regra mudar no monólito antes da remoção,
mudar aqui também.

| Aqui | Origem no MechanicLtda |
|---|---|
| `Application/DTOs/ResponseDto.cs` | `MechanicLtda.Application/DTOs/ResponseDto.cs` — idêntico, de propósito: o host Web já lê `hasErrors`/`getErrors`/`getResponse` |
| `Application/Security/JwtSecretProvider.cs` | `MechanicLtda.Application/Security/JwtSecretProvider.cs` |
| `API/Middlewares/CorrelationIdMiddleware.cs` | `MechanicLtda.API/Middlewares/CorrelationIdMiddleware.cs` |
| `API/Extensions/AuthenticationExtension.cs` | `MechanicLtda.API/Extensions/AuthenticationExtension.cs` — mesma chave simétrica e os mesmos dois emissores (`Issuer` e `IssuerCpf`) |
| `API/Authorization/Roles.cs` | `MechanicLtda.Bootstrap/Authorization/Roles.cs` |
| `Domain/Entities/Base/Entity.cs` | `MechanicLtda.Domain/Entities/Base/Entity.cs` |
| `Infrastructure/Repositories/Base/Repository.cs` | `MechanicLtda.Infrastructure/Repositories/Base/Repository.cs` — ver a adaptação na seção 3 |
| `Application/AppServices/OrcamentoPdfAppService.cs` | `MechanicLtda.Application/AppServices/OrcamentoPdfAppService.cs` — mesmo layout, cabeçalho, seções e rodapé |
| `Program.cs` (rotas minúsculas, `ForwardedHeaders`, `/health`) | `MechanicLtda.API/Program.cs` |
| `src/OrcamentoPagamento.API/Dockerfile` | `src/MechanicLtda.API/Dockerfile` — inclusive as dependências nativas do QuestPDF/SkiaSharp e a fonte DejaVu |
| `.github/workflows/main.yml` | `.github/workflows/main.yml` — mesmo contrato de build, testes, push no ECR e dispatch do deploy |

## 3. Portado com adaptação

| Aqui | Origem | O que mudou e por quê |
|---|---|---|
| `Domain/Services/OrcamentoService.cs` → `CalcularAsync` | `OrcamentoService.CriarOuAtualizarAsync` | Lá o cálculo busca a OS, os itens e consulta o `Estoque` de cada item para descobrir se é peça ou insumo. Aqui recebe tudo pronto no comando: **nenhuma consulta a outro contexto**. Era o acoplamento que impedia a separação |
| idem → `AtualizarManualAsync` | `OrcamentoService.AtualizarManualAsync` | Ganhou `ValorTotalServicos`, validação de valor negativo e publicação de evento. O total geral passou a ser a soma das **três** categorias |
| `Domain/Entities/Orcamento.cs` | `MechanicLtda.Domain/Entities/Orcamento.cs` | Perdeu a navegação e a FK para `OrdemServico` (outro banco), ganhou `ValorTotalServicos`, itens, snapshot, marcas de ajuste manual e os campos de idempotência. A regra de totalização virou método do agregado (`RecalcularTotais`) em vez de ficar solta no serviço |
| `Domain/Entities/ItemOrcamento.cs` | `MechanicLtda.Domain/Entities/ItemOrdemServico.cs` | Cópia local e enxuta: só o que o orçamento precisa (descrição, categoria, quantidade, valor). Sem FK para `Estoque`/`ServicoOficina`, com `ItemOrigemId` guardando o id do item na OS. `CalcularValorTotal()` veio igual |
| `Application/AppServices/OrcamentoAppService.cs` | `MechanicLtda.Application/AppServices/OrcamentoAppService.cs` | Mesmo padrão de `try`/`catch` e `ResponseDto`. Ganhou `RecalcularAsync`, que traduz o evento em comando de domínio |
| `Application/DTOs/OrcamentoDtos.cs` | `MechanicLtda.Application/DTOs/OrcamentoDtos.cs` | `OrcamentoDto` ganhou itens, `ValorTotalServicos` e as marcas de ajuste. `OrcamentoPdfDto` passou a ter campos anuláveis, porque o snapshot pode não ter vindo no evento |
| `API/Controllers/OrcamentoController.cs` | `MechanicLtda.API/Controllers/OrcamentoController.cs` | Mesmas 5 rotas, mesmos verbos, mesmo envelope. Ganhou `POST /recalcular`. `ObterPorId` passou a responder `404` em vez de `400` quando não existe |
| `Infrastructure/Repositories/OrcamentoRepository.cs` | `MechanicLtda.Infrastructure/Repositories/OrcamentoRepository.cs` | Os `Include` deixaram de ser de `OrdemServico`/`Cliente`/`Veiculo` e passaram a ser dos itens e do snapshot locais |
| `Infrastructure/Repositories/Base/Repository.cs` → `AtualizarAsync` | idem do monólito | Lá o `ObterPorIdAsync` desanexa a entidade e o `AtualizarAsync` sempre chama `Update()`. Aqui o orçamento é carregado **rastreado com os filhos**, porque o cálculo limpa e recria os itens e o EF precisa enxergar isso para apagar os órfãos. Então `AtualizarAsync` só chama `Update()` se a entidade estiver `Detached` |
| `Domain/Interfaces/Services/INotificadorService.cs` e `Services/Base/BaseService.cs` | equivalentes do monólito | Mesmo papel, contrato simplificado: lá a falha é a entidade `Notificacao`, que alimenta o painel de integrações; aqui é string, porque este serviço não tem esse painel nem a tabela. `BaseService` também perdeu o `IConfiguration`, que não era usado pelo orçamento |
| `Infrastructure/FluentAPI/` | `MechanicLtda.Infrastructure/FluentAPI/` | Mesma ideia de uma classe `Config` por tabela herdando de `ConfiguracaoContextoBase`. Lá a aplicação acontece dentro do `BancoAPIContext`; aqui há um `ConfiguracaoContexto` separado com a lista explícita das 4 configs |
| `API/Extensions/DatabaseExtension.cs` | `MechanicLtda.Bootstrap/Extensions/DatabaseExtension.cs` + `MigrationExtension.cs` | Juntados num só e sem ASP.NET Identity — este serviço não tem usuários. O `MigrateDatabaseAsync` ganhou o desvio para provider não relacional, usado pelos testes de integração |

Não existe projeto `Bootstrap` aqui: com um único host, a composição ficou nas extensões do
próprio `API`.

## 4. Não existia no monólito

| Aqui | Por que existe |
|---|---|
| `Domain/Entities/OrdemServicoSnapshot.cs` | O PDF precisa de cliente, veículo e descrição do problema. No monólito isso vinha de um `Include` em três tabelas; aqui elas estão em outro banco, então os dados chegam no evento e ficam congelados. Efeito colateral desejável: um PDF reemitido reproduz o orçamento como ele era |
| `Domain/Enums/CategoriaItem.cs` | Substitui a consulta ao `TipoEstoque` do item. A classificação passa a ser resolvida por quem publica o evento |
| `Orcamento.UltimoEventoId` / `UltimoEventoEm` e `DeveDescartarEvento` | O SQS entrega ao menos uma vez e sem garantia de ordem. Resolve reentrega e evento atrasado sem tabela de controle extra |
| `Domain/Commands/CalcularOrcamentoCommand.cs` | Entrada única do cálculo, usada tanto pela fila quanto pelo endpoint de recálculo — o domínio não sabe de onde o pedido veio |
| `Application/Eventos/OrdemServicoItensAlteradosEvent.cs` | O contrato do evento consumido |
| `Domain/Interfaces/Messaging/IPublicadorEventos.cs` + `SnsPublicadorEventos` | Publicação de `orcamento.calculado`/`ajustado`/`removido`, com `MessageAttribute` de tipo para filter policy |
| `Infrastructure/Messaging/PublicadorEventosLog.cs` | Com `Messaging:Habilitado = false` o serviço sobe sem tocar na AWS. É o modo usado em dev e nos testes |
| `API/Messaging/SqsConsumidorItensAlterados.cs` | A entrada principal do serviço. Fica no host, e não na `Infrastructure`, porque precisa da `Application` — e `Infrastructure` depende só do `Domain` |
| `POST /api/orcamento/recalcular` | Porta síncrona do mesmo cálculo: reprocessar mensagem da DLQ, operar sem mensageria, ou quando o chamador precisa do orçamento na mesma requisição |
| `scripts/localstack/` + LocalStack no compose | Reproduz tópicos, fila e DLQ localmente, com os mesmos SDKs e a mesma configuração de ARN/URL da produção |
| `.config/dotnet-tools.json` | `dotnet-ef` fixado como ferramenta local, para a migration ser reproduzível em qualquer máquina e na CI |

### 4.1 Módulo de pagamento

Todo o módulo é código novo — **nada veio do monólito**, que não tem pagamento. O que foi
reaproveitado é o *padrão*, não o código:

| Aqui | Padrão que seguiu | Por que |
|---|---|---|
| `IGatewayPagamento` (porta no `Domain`) | `IPublicadorEventos` | O domínio não conhece o Mercado Pago |
| `MercadoPagoGateway` | `SnsPublicadorEventos` | Único arquivo que fala o vocabulário do provedor: nomes de campo, strings de status, formato de data |
| `GatewayPagamentoFake` | `PublicadorEventosLog` | Roda o fluxo inteiro sem o provedor. Decide o resultado pelo token (`APRO`/`FUND`/`CONT`) |
| `Pagamentos:Habilitado` | `Messaging:Habilitado` | Mesmo interruptor para desligar dependência externa |
| `Pagamento.UltimaAtualizacaoProvedor` | `Orcamento.UltimoEventoEm` | Mesma defesa contra mensagem fora de ordem, com outro relógio |
| `Pagamento.Valor` congelado | `OrdemServicoSnapshot` | O documento retrata o que foi acordado, não o que os dados dizem hoje |
| `PagamentoController` + `ResponseDto` | `OrcamentoController` | O Web lê o mesmo envelope |

Duas coisas que o pagamento tem e o orçamento não:

- **Máquina de estados** (`Pagamento.PodeTransicionarPara`). O orçamento é recalculado do
  zero a cada evento; o pagamento tem estados terminais e caminhos proibidos — sem isso,
  uma notificação atrasada faria um pagamento aprovado voltar a pendente.
- **Rota pública** (`WebhookMercadoPagoController`), com validação de `x-signature` por
  HMAC-SHA256. É a única rota do serviço sem JWT.

E uma regra que atravessa os dois módulos: `OrcamentoService.CalcularAsync` e
`AtualizarManualAsync` chamam `IPagamentoService.CancelarEmAbertoAsync` quando o valor muda.
É a única dependência entre os módulos, e só nessa direção — `PagamentoService` usa
`IOrcamentoRepository`, nunca `IOrcamentoService`, o que evita ciclo de injeção.

## 5. Deliberadamente não veio

Nada disto foi espelhado, e não deve ser adicionado sem revisar a
[ADR-001](adr/ADR-001-extracao-do-orcamento-como-microsservico.md):

| O que | Por quê |
|---|---|
| `Cliente`, `Veiculo`, `OrdemServico`, `ItemOrdemServico`, `Estoque`, `ServicoOficina` | Pertencem a outros contextos. O que o orçamento precisa deles chega no evento |
| ASP.NET Core Identity, `Usuario`, login, troca de senha | Este serviço **valida** token, não emite. Autenticação segue na API e na Lambda |
| Criptografia e índice cego de CPF/CNPJ | Nenhum dado pessoal sensível é persistido aqui além de nome, e-mail e telefone no snapshot |
| Envio de e-mail (MailKit) | O orçamento não notifica ninguém por e-mail; quem faz isso é o fluxo de status da OS |
| Seed de dados | Orçamento só existe a partir de um evento. Banco vazio é o estado correto na primeira subida |
| Agente APM do New Relic no Dockerfile | Observabilidade do serviço ainda não foi decidida; por ora, log estruturado com `CorrelationId` |
| Host Web / views Razor | Este repositório é só o serviço. A UI segue no monólito |

## 6. Testes

| Projeto | O que cobre |
|---|---|
| `OrcamentoPagamento.Domain.Tests` (70) | Totais por categoria; substituição de itens no recálculo; idempotência por evento repetido e por evento atrasado; gravação do snapshot; orçamento sem itens; ajuste manual somando as categorias; recusa de valor negativo; recálculo descartando ajuste manual anterior; publicação dos eventos; falha de publicação não desfazendo o cálculo |
| `OrcamentoPagamento.API.IntegrationTests` (32) | `/health` público; `401` sem token; `403` com role `Cliente`; `404` sem orçamento; recálculo criando o orçamento; `204` por idempotência; fluxo recalcular → consultar → ajustar → PDF; `400` em valor negativo; remoção; devolução do `X-Correlation-Id` |

Os de integração sobem a API real com `WebApplicationFactory`, banco InMemory e mensageria
desligada.

## 7. Referências

| Documento | Assunto |
|---|---|
| [ADR-001](adr/ADR-001-extracao-do-orcamento-como-microsservico.md) | Por que extrair o orçamento e por que banco por serviço |
| [ADR-002](adr/ADR-002-contrato-de-eventos.md) | Contrato do evento, idempotência e tratamento de erro no consumo |
| [ADR-003](adr/ADR-003-consumo-pelo-web-via-api-gateway.md) | Como o host Web consome este serviço |
| [Integração](integracao.md) | O que ainda falta nos outros repositórios para este serviço entrar em uso |
