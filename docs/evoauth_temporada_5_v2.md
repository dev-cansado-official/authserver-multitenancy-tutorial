---
id: evoauth-temporada-5
title: "Temporada 5 — Isolamento de Dados por Tenant (Alinhado ao modelo SaaSApp)"
sidebar_position: 5
---

# Temporada 5 — Isolamento de Dados por Tenant

⚠️ Esta versão já está alinhada ao modelo corrigido da Temporada 4:

- OAuth (OpenIddictApplication) = autenticação
- SaaSApp = módulo do produto
- TenantId = isolamento de dados

## Conceitos Separados

- **TenantId** → Isola dados físicos (Orders, Customers, etc)
- **SaaSApp** → Isola autorização/feature set
- **Client (OAuth)** → Apenas autentica

Tenant e SaaSApp NÃO são a mesma coisa.

---

# O que NÃO muda

A implementação de:

- ITenantOwnedEntity
- QueryFilter global no DbContext
- Enforcer no SaveChanges
- Middleware TenantResolver

continua exatamente igual.

Isolamento físico é independente de SaaSApp.

---

# O que muda conceitualmente

Se um endpoint for específico de um módulo:

Exemplo:

GET /apps/{appKey}/orders

Agora:

- TenantId isola dados
- appKey define o módulo
- Permissão é validada contra SaaSApp
- ClientAllowedApp valida se o client pode acessar o módulo

---

# Regra Arquitetural Final

Banco de Dados:
- TenantId → isolamento físico

Autorização:
- SaaSApp → escopo de permissões

OAuth:
- Client → obtém token

---

# Segurança Final

Mesmo que:

- Client esteja autenticado
- Usuário tenha permissão

Se TenantId for diferente → QueryFilter bloqueia.

Se Client não estiver vinculado ao SaaSApp → Endpoint de permissões deve bloquear.

---

Temporada 6 complementa isso com cache distribuído e hardening.
