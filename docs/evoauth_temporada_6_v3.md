---
id: evoauth-temporada-6
title: "Temporada 6 — Hardening (RBAC por Tenant + ClientId)"
sidebar_position: 6
tags: [dotnet, openiddict, security, redis, observability, ratelimit]
---

# Temporada 6 — Hardening (RBAC por Tenant + ClientId)

Modelo final:

- OAuth client = `OpenIddictApplication` (`client_id`)
- Autorização = **RBAC por TenantId + ClientId**
- Isolamento de dados = TenantId (QueryFilter)

---

# 1) Access Token: evitar ID2004 em APIs separadas

## Tutorial/DEV (recomendado)

No AuthServer:

```csharp
options.AddDevelopmentSigningCertificate();
options.AddEphemeralEncryptionKey();
options.DisableAccessTokenEncryption(); // ✅ access_token vira JWS (3 partes)
```

Motivo:
- API está em projeto separado
- Evita `invalid_token (ID2004)` quando o token vinha como JWE

## Produção

Opções:
- Manter access_token como JWS (assinado) — comum e suficiente
- Ou criptografar, mas aí precisa planejar compartilhamento/validação de chaves entre serviços

---

# 2) Cache distribuído (Redis) por tenant + client

Chave correta:

`perm:{userId}:{tenantId}:{clientId}`

Por quê:
- client_id representa a aplicação do cliente
- permissões podem variar por app dentro do mesmo tenant

---

# 3) Revogação rápida (versionamento por tenant+client)

Recomendação:

`UserTenantClientSecurityVersion (UserId, TenantId, ClientId, Version)`

Cache key:

`perm:{userId}:{tenantId}:{clientId}:v{version}`

Ao alterar role/permissões do usuário naquela app:
- incrementa `Version`
- o cache automaticamente “morre”

---

# 4) Rate limit

PartitionKey = `tenantId`.
Se quiser granularidade maior: `tenantId:clientId`.

---

# 5) Observabilidade

Inclua:
- TenantId
- ClientId
- UserId (sub)
- TraceId

---

# 6) Fluxos: Password → Code+PKCE

Quando existir UI (SPA/mobile), migre para:
- Authorization Code + PKCE
- Refresh token com expiração e revogação

O RBAC continua igual — só muda o fluxo de autenticação.
