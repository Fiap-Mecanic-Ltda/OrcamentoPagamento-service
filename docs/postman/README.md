# Coleção Postman — microsserviço de Orçamento

`Orcamento.postman_collection.json` cobre o fluxo completo do serviço, com testes
automáticos em cada requisição.

## Como usar

1. Importe a coleção no Postman.
2. Ajuste as variáveis da coleção:

   | Variável | Padrão | Uso |
   |---|---|---|
   | `orcamentoBaseUrl` | `http://localhost:8081` | URL do microsserviço. Em produção, a base do API Gateway |
   | `authBaseUrl` | `http://localhost:8080` | API do monólito, que emite o token |
   | `email` / `senha` | `admin@mechanic.com` / `Admin@123` | Credenciais do seed do monólito |
   | `ordemServicoId` | `12` | OS usada no recálculo |

3. Rode na ordem (ou use **Run collection**). As requisições encadeiam estado: o login
   guarda o `token` e o recálculo guarda o `orcamentoId`.

## O que cada requisição demonstra

| # | Requisição | Verifica |
|---|---|---|
| 0 | Health | Serviço de pé, sem autenticação |
| 1 | Login no monólito | O serviço não emite token: ele vem do monólito ou da Lambda |
| 2 | Consulta sem token | `401` — rota protegida |
| 3 | Recalcular | Cria o orçamento e separa os totais por categoria (200/100/150, total 450) |
| 4 | Recalcular com evento atrasado | `204` — idempotência descarta sem sobrescrever |
| 5 | Obter por OS | O valor continua 450, provando que o evento atrasado não passou |
| 6 | Obter por Id | Consulta direta, com itens |
| 7 | Ajuste manual | Total geral vira a soma das três categorias (500) e marca `ajustadoManualmente` |
| 8 | Ajuste com valor negativo | `400` |
| 9 | Exportar PDF | `application/pdf`, gerado a partir do snapshot local |
| 10 | Remover | `200`, e publica `orcamento.removido` |

> Sem o monólito rodando, pule a requisição 1 e cole um JWT válido direto na variável
> `token`. A chave de assinatura precisa ser a mesma `JWT_SECRET_KEY` do serviço.
