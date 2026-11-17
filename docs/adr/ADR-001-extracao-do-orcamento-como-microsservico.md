# ADR-001 — Extração do Orçamento como microsserviço, com banco próprio

- **Status:** aceito
- **Data:** 2026-11-06
- **Substitui parcialmente:** [ADR-005 do MechanicLtda](https://github.com/Fiap-Mecanic-Ltda/MechanicLtda/blob/main/docs/adr/ADR-005-comunicacao-sincrona-rest.md)

## Contexto

Até a Fase 3, o MechanicLtda era um monólito modular com dois hosts (API REST e Web MVC)
compartilhando `Application`/`Domain` no mesmo processo e um único banco SQL Server. A
ADR-005 daquele repositório registrou essa escolha e descartou microsserviços na época:
"um único contexto de negócio e um único banco: dividir traria latência e falhas de rede
sem ganho de autonomia de deploy".

O que mudou:

1. O orçamento é o ponto do sistema com **regra de cálculo própria e evolução mais
   frequente** (totais por categoria, validade, ajuste manual, PDF) e nenhuma escrita em
   dados de outros contextos — ele só lê itens da OS e o tipo do estoque.
2. A geração do PDF carrega **QuestPDF/SkiaSharp e fontes nativas**, que hoje pesam na
   imagem e na memória de todos os pods da API, inclusive os que só atendem CRUD.
3. O orçamento é recalculado a cada alteração de item. No monólito isso acontece **dentro
   da requisição** que altera o item (`ItemOrdemServicoService` chama
   `OrcamentoService.CriarOuAtualizarAsync`), somando latência a uma operação que o
   usuário percebe.

## Decisão

1. **O orçamento passa a ser um serviço próprio**, em repositório próprio
   (`OrcamentoPagamento-service`), com imagem, pipeline e ciclo de deploy independentes.
2. **Banco por serviço.** `MechanicOrcamentoDb`, separado do banco do monólito, com
   migrations próprias. Não há chave estrangeira para `OrdensServico`: o vínculo é o
   `OrdemServicoId` mais um índice único, que mantém a regra "uma OS, um orçamento"
   dentro do serviço.
3. **O serviço não consulta outros serviços para funcionar.** Os itens chegam já
   classificados (`Peca`, `Insumo`, `Servico`) no evento, e os dados de OS/cliente/veículo
   necessários ao PDF ficam num snapshot local (`OrdemServicoSnapshots`). Ver
   [ADR-002](ADR-002-contrato-de-eventos.md).
4. **A entrada principal é assíncrona** (fila SQS), com um endpoint REST de recálculo como
   porta alternativa para reprocessamento e para operação sem mensageria.
5. **O serviço não emite tokens.** Valida os JWT do login da API e da Lambda de CPF, com a
   mesma chave simétrica e os mesmos dois emissores.

## Alternativas

| Alternativa | Por que não |
|---|---|
| Manter o orçamento no monólito | Não resolve nenhum dos três pontos do contexto; mantém o cálculo e o PDF acoplados ao deploy de todo o resto |
| Microsserviço com banco compartilhado | Qualquer migration do monólito passaria a poder quebrar o orçamento, e vice-versa: é monólito distribuído, com o custo da rede e sem a autonomia |
| Orçamento lendo itens da OS por HTTP a cada cálculo | A indisponibilidade da Oficina passaria a derrubar o orçamento, e o PDF de um orçamento antigo mudaria junto com os dados de hoje |
| Separar Orçamento e Itens em serviços distintos | O orçamento é derivado dos itens; cada item adicionado exigiria uma saga para manter os dois lados consistentes |

## Consequências

- **Consistência eventual.** Entre alterar um item na OS e o orçamento refletir isso existe
  uma janela (publicação + fila + processamento). A tela de detalhe da OS precisa tolerar
  ler um orçamento ainda não atualizado. Quando a resposta imediata for obrigatória, o
  chamador usa `POST /api/orcamento/recalcular`, que é síncrono.
- **Snapshot pode divergir.** Se o cliente trocar de telefone e nenhum item da OS mudar, o
  PDF continua com o telefone antigo até o próximo recálculo. Para um documento de
  orçamento isso é desejável: ele retrata o que foi orçado.
- O monólito ganha uma responsabilidade nova: **publicar o evento** de itens alterados.
  Enquanto ele não publicar, o orçamento só é calculado pela rota de recálculo.
- Rastreio distribuído: o `X-Correlation-Id` da requisição original viaja no evento e entra
  no escopo de log do consumidor, preservando o rastreio de ponta a ponta já existente.
- Duas rotas de orçamento passam a existir em paralelo (a do monólito e a deste serviço)
  até o host Web ser apontado para o API Gateway. O desligamento das rotas antigas do
  monólito é um passo posterior, não parte desta ADR.
- O nome do repositório prevê **Pagamento** no mesmo serviço. Esta ADR cobre apenas o
  orçamento; incluir pagamento exigirá avaliar se é o mesmo contexto ou um serviço à parte.
