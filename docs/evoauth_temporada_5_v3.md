---
id: evoauth-temporada-5
title: "Temporada 5 — Isolamento de Dados por Tenant (alinhada ao RBAC por ClientId)"
sidebar_position: 5
tags: [dotnet, multitenant, efcore, sqlserver, isolation]
---

# Temporada 5 — Isolamento de Dados por Tenant

Esta temporada permanece praticamente igual, mas alinhada ao modelo definitivo da Temporada 4:

- **TenantId** → isolamento físico de dados
- **ClientId (OAuth)** → contexto de autorização (permissões por app do cliente)
- **Permissões** → RBAC por tenant + client_id

## Separação correta

Você sempre precisa dos dois:
- TenantId para **isolar dados**
- client_id para **isolar permissões por aplicação**

---

## O que NÃO muda no código

- `ITenantOwnedEntity`
- QueryFilter global no DbContext
- Enforcer no `SaveChanges`
- Middleware `TenantResolver` (X-Tenant-Id)

---

## Como conversa com a Temporada 4

1) API valida token  
2) API resolve TenantId (header)  
3) API valida permissão `[HasPermission("x.y")]`  
   - cache key: `perm:{sub}:{tenantId}:{clientId}`  
   - consulta AuthServer: `/tenants/{tenantId}/me/permissions`  
4) EF QueryFilter garante que dados do tenant não vazem  

✅ Mesmo com token válido e permissão válida, TenantId errado bloqueia dados.

---

## Próximo passo (Temporada 6)
- Redis para cache distribuído
- Revogação por tenant+client
- Observabilidade com TenantId + ClientId
