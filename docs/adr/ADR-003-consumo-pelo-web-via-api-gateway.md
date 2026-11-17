# ADR-003 — O host Web consome o microsserviço pelo API Gateway

- **Status:** aceito
- **Data:** 2026-11-06

## Contexto

Hoje o `MechanicLtda.Web` (Razor/MVC) chama `IOrcamentoAppService` **em processo**, pelo
composition root compartilhado (`MechanicLtda.Bootstrap`). Com o orçamento fora do
monólito, esse host precisa de um caminho de rede até o serviço.

O projeto já tem um API Gateway HTTP API na frente da API (Fase 3), que resolve
autenticação na borda, TLS, e injeta `X-Correlation-Id` a partir do seu `$context.requestId`
([ADR-001 do MechanicLtda](https://github.com/Fiap-Mecanic-Ltda/MechanicLtda/blob/main/docs/adr/ADR-001-api-gateway-http-api.md)).

## Decisão

1. **O Web chama o API Gateway**, que roteia por path:

   ```text
   Browser → Web (Razor) → API Gateway (HTTP API)
                             ├─ /api/orcamento/*  → orcamento-service (este repo)
                             └─ /api/*            → mechanicltda-api (monolito)
   ```

2. **O contrato de resposta é o mesmo `ResponseDto`** do monólito (`hasErrors`,
   `getErrors`, `getResponse`). As views Razor não mudam de formato; só a origem do dado
   muda, de chamada em processo para HTTP.

3. **As rotas são minúsculas** (`LowercaseUrls`), porque o roteamento do API Gateway é
   *case-sensitive* — mesma regra já adotada na API.

4. **O Web propaga o token do usuário** no `Authorization` da chamada. O microsserviço
   valida o mesmo JWT, com a mesma chave e os mesmos dois emissores; não existe credencial
   de serviço separada.

5. **Sem BFF agregador por enquanto.** A tela de orçamento usa uma rota só, então um salto
   extra de agregação não se paga. Se uma tela passar a precisar de dados de três serviços
   numa mesma renderização, aí se reavalia.

## Alternativas

| Alternativa | Por que não agora |
|---|---|
| BFF/agregador entre o Web e os serviços | Mais um componente para operar e mais um salto de rede, sem tela que hoje precise de fan-out |
| Web chamando o Service do Kubernetes direto (`http://orcamento-api:8080`) | Dispensaria o gateway, mas duplicaria a autenticação de borda e tiraria a observabilidade e o `X-Correlation-Id` que o gateway já dá |
| Manter o Web em processo com o orçamento | Só funcionaria mantendo o código do orçamento dentro do monólito, o que anula a [ADR-001](ADR-001-extracao-do-orcamento-como-microsservico.md) |
| gRPC entre Web e microsserviço | O Web já fala HTTP/JSON com o gateway; contrato binário não resolve problema existente |

## Consequências

- O Web precisa de `HttpClient` tipado com a URL base do gateway em configuração, e de
  tratamento de timeout/erro de rede onde antes havia chamada em processo que não falhava
  por rede. Recomendado `HttpClientFactory` com retry para os `GET` (idempotentes) e **sem
  retry automático** no `PUT`/`DELETE`.
- Uma indisponibilidade do microsserviço aparece como erro na tela de orçamento, sem afetar
  o resto do Web — que é o ganho de isolamento pretendido.
- O caminho da requisição ganha um salto, com o custo de latência do gateway.
- A rota antiga `/api/orcamento/*` do monólito continua respondendo até ser desligada; o
  roteamento do gateway é o que decide qual das duas atende, então a virada é feita lá, sem
  mudança de código no Web.
