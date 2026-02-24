---
id: evoauth-temporada-4
title: "Temporada 4 — RBAC por Tenant + ClientId (Dogfooding pronto e separação futura)"
description: "Autorização genérica para apps de clientes: roles/permissões por tenant e por aplicação (OpenIddict client_id), com bootstrap do SaaS se usando."
sidebar_position: 4
tags: [dotnet, openiddict, multitenant, authorization, rbac, roadmap]
---

# Temporada 4 — RBAC por Tenant + ClientId (Dogfooding pronto)

Nesta temporada você consolida o modelo correto para um **SaaS de AuthN/AuthZ** que:
- atende **apps de clientes** (qualquer SPA/mobile/backend/integrador);
- e **se usa a si mesmo** (dogfooding) como “uma organização qualquer”.

## Decisão oficial (para não se perder)
- **AuthN (OpenIddict)** e **AuthZ (RBAC)** ficam **no mesmo projeto/banco por enquanto** (MVP).
- O código fica organizado para **separação futura** (AuthServer.Oidc vs AuthServer.Rbac).

### O que NÃO existe mais
- ❌ `SaaSApp`
- ❌ `ClientAllowedApp`
- ❌ “permissões por appKey” fora do `client_id`

**A aplicação (para autorização) é o próprio OAuth client:** `client_id`.

---

# 1) Contratos oficiais (rotas)

## 1.1 Minhas permissões (para APIs de negócio)
**GET** `/tenants/{tenantId}/me/permissions`

- Resolve `sub` (usuário) e `client_id` do token.
- Usa `tenantId` da rota.
- Retorna as permissões efetivas do usuário **naquele tenant e naquele client**.

Resposta:

```json
{
  "tenantId": "…",
  "clientId": "…",
  "permissions": ["orders.read", "orders.cancel"]
}
```

> Este endpoint é o “núcleo” do `HasPermission` na API (com cache).

## 1.2 Administração (RBAC mínimo)
Para o **Admin Portal do próprio SaaS** administrar RBAC:

- **POST** `/tenants/{tenantId}/roles`
- **GET**  `/tenants/{tenantId}/roles?clientId=...`
- **POST** `/tenants/{tenantId}/roles/{roleId}/permissions`
- **POST** `/tenants/{tenantId}/users/{userId}/roles`

---

# 2) Modelo de Dados (AuthServer)

> Tudo aqui vive no AuthServer (mesmo DbContext do OpenIddict/Identity).

## 2.1 Permission (catálogo global)

```csharp
public class Permission
{
    public Guid Id { get; set; }
    public string Key { get; set; } = default!; // ex: "orders.read"
    public string Description { get; set; } = default!;
}
```

## 2.2 Role (por Tenant + ClientId)

```csharp
public class Role
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    // ✅ aplicação = client OAuth (OpenIddictApplication.ClientId)
    public string ClientId { get; set; } = default!;

    public string Name { get; set; } = default!; // ex: "GlobalAdmin", "Viewer"
}
```

## 2.3 RolePermission (N:N)

```csharp
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = default!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = default!;
}
```

## 2.4 UserTenantRole (N:N com contexto de tenant)

```csharp
public class UserTenantRole
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = default!;
}
```

---

# 3) DbContext + Constraints

No `AuthDbContext`:

```csharp
public DbSet<Permission> Permissions => Set<Permission>();
public DbSet<Role> Roles => Set<Role>();
public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
public DbSet<UserTenantRole> UserTenantRoles => Set<UserTenantRole>();

protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);

    builder.Entity<Permission>()
        .HasIndex(x => x.Key)
        .IsUnique();

    builder.Entity<Role>()
        .HasIndex(x => new { x.TenantId, x.ClientId, x.Name })
        .IsUnique();

    builder.Entity<RolePermission>()
        .HasKey(x => new { x.RoleId, x.PermissionId });

    builder.Entity<UserTenantRole>()
        .HasKey(x => new { x.UserId, x.TenantId, x.RoleId });
}
```

Migration:

```bash
dotnet ef migrations add AddRbacByTenantClient
dotnet ef database update
```

---

# 4) Token: padronizar claim client_id

No `ConnectController`, após montar `principal`:

```csharp
var clientId = request.ClientId;
if (!string.IsNullOrWhiteSpace(clientId))
{
    principal.SetClaim("client_id", clientId);
}
```

E mantenha seus destinos (AccessToken).

> Assim você não depende de claims “alternativas” (`oi_cl_id`).

---

# 5) Endpoint “me/permissions” (AuthServer) — copy/paste

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("tenants")]
[Authorize]
public class PermissionsController : ControllerBase
{
    private readonly AuthDbContext _db;
    public PermissionsController(AuthDbContext db) => _db = db;

    [HttpGet("{tenantId:guid}/me/permissions")]
    public async Task<IActionResult> GetPermissions(Guid tenantId, CancellationToken ct)
    {
        var sub = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userId))
            return Unauthorized();

        var clientId = User.FindFirst("client_id")?.Value;
        if (string.IsNullOrWhiteSpace(clientId))
            return Forbid();

        var permissions = await _db.UserTenantRoles
            .Where(utr => utr.UserId == userId && utr.TenantId == tenantId)
            .Join(_db.Roles,
                utr => utr.RoleId,
                r => r.Id,
                (utr, r) => new { r.Id, r.ClientId })
            .Where(x => x.ClientId == clientId)
            .Join(_db.RolePermissions,
                x => x.Id,
                rp => rp.RoleId,
                (x, rp) => rp.PermissionId)
            .Join(_db.Permissions,
                pid => pid,
                p => p.Id,
                (pid, p) => p.Key)
            .Distinct()
            .ToListAsync(ct);

        return Ok(new
        {
            tenantId,
            clientId,
            permissions
        });
    }
}
```

---

# 6) Dogfooding (Bootstrap no SeedAsync)

Objetivo: o SaaS nasce com uma “org interna” e um Admin Portal que já usa RBAC.

## 6.1 Convenções
- Tenant interno: `internal`
- Client do Admin Portal: `opentenid-admin-portal`
- Role: `GlobalAdmin`

## 6.2 Permissões mínimas do admin portal
- `tenants.create`, `tenants.read`, `tenants.update`
- `users.invite`, `users.read`, `users.disable`
- `roles.create`, `roles.assign`, `permissions.manage`
- `clients.manage`

## 6.3 Seed (pseudo-código bem direto)

1) Criar tenant interno (`Tenant`/`Organization` — conforme sua entidade)
2) Criar OpenIddictApplication `opentenid-admin-portal`
3) Garantir permissões do admin portal no catálogo (`Permissions`)
4) Criar role `GlobalAdmin` com `(TenantId=internal, ClientId=opentenid-admin-portal)`
5) Criar RolePermission para todas as permissões acima
6) Vincular usuário seed (ex: `demo@local`) ao GlobalAdmin via `UserTenantRole`

> Resultado: sua UI/admin (quando existir) já nasce autorizada como qualquer cliente.

---

# 7) Separação futura (sem reescrever tudo depois)

Mesmo estando tudo junto hoje, organize namespaces/pastas assim:

- `AuthServer.Oidc/*` (OpenIddict + endpoints /connect)
- `AuthServer.Rbac/*` (entidades Role/Permission + endpoints /tenants/*)
- `AuthServer.Tenancy/*` (Tenant/Organization + resolução)
- `AuthServer.Infrastructure/*` (DbContext, migrations, seed)

Quando separar em serviços depois:
- RBAC vira um serviço “Authorization”
- OIDC vira “Identity”
- O contrato `/tenants/{tenantId}/me/permissions` continua igual

---

# 8) Checkpoints (validação)

1) Obter token com `client_id=opentenid-admin-portal` e `scope=api`
2) Chamar `GET /tenants/{internalTenantId}/me/permissions` no AuthServer
3) Confirmar que retorna as permissões do GlobalAdmin
4) Na API de negócio:
   - cache key: `perm:{sub}:{tenantId}:{clientId}`
   - `[HasPermission("tenants.read")]` deve permitir

---

## Próximo passo
- **Temporada 5**: isolamento EF por TenantId (mantém)
- **Temporada 6**: Redis + revogação rápida por tenant+client + observabilidade
- **Temporada 7**: Admin Portal mínimo (UI) para administrar RBAC
