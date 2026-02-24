---
id: evoauth-temporada-6
title: "Temporada 6 — Hardening (Alinhado ao modelo SaaSApp)"
sidebar_position: 6
---

# Temporada 6 — Hardening Final

Agora o modelo completo é:

OAuth → OpenIddictApplication  
Autorização → SaaSApp  
Isolamento → TenantId  

---

# 1) Access Token (Tutorial vs Produção)

## DEV / Tutorial

No AuthServer:

options.AddDevelopmentSigningCertificate();  
options.AddEphemeralEncryptionKey();  
options.DisableAccessTokenEncryption();

Motivo:
- API está em projeto separado
- Evita ID2004 (invalid_token)
- Access token vira JWS (3 partes)

## Produção

Opções:
- Compartilhar chave de encryption entre serviços
- Ou manter DisableAccessTokenEncryption e usar apenas assinatura

---

# 2) Cache de Permissões (Redis)

Cache Key recomendada:

perm:{userId}:{tenantId}:{appKey}

Onde:

- userId → claim sub  
- tenantId → header X-Tenant-Id  
- appKey → SaaSApp (módulo)

NUNCA usar client_id como chave de permissão.

---

# 3) Revogação Avançada

Modelo recomendado:

UserTenantAppSecurityVersion  
(UserId, TenantId, AppId, Version)

Cache key final:

perm:{userId}:{tenantId}:{appKey}:v{version}

Isso permite revogar permissões por módulo sem invalidar tudo.

---

# 4) Validar ClientAllowedApp

No endpoint de permissões (AuthServer):

1. Extrair client_id do token  
2. Verificar tabela ClientAllowedApp  
3. Se não existir vínculo → 403  

Isso impede que um client OAuth peça permissões de módulo não autorizado.

---

# 5) Rate Limiting por Tenant

Continuar usando chave:

partitionKey = tenantId

SaaSApp não interfere em rate limit (a menos que você queira granularidade maior).

---

# 6) Observabilidade

Logs devem conter:

TenantId  
AppKey (SaaSApp)  
ClientId  
TraceId  

---

# Arquitetura Final Consolidada

Usuário autentica → Client (OAuth)  
↓  
Token emitido  
↓  
API valida token  
↓  
API valida TenantId  
↓  
API consulta permissões por SaaSApp  
↓  
QueryFilter garante isolamento físico  

---

Agora o modelo está:

✔ Separado  
✔ Escalável  
✔ SaaS-ready  
✔ Multi-tenant real  
✔ Multi-client real  
