---
id: evoauth-temporada-6
title: "Temporada 6 — Hardening: Segurança, Revogação, Cache, Observabilidade"
description: "Leva o AuthServer + API para nível produção com PKCE, rotação de chaves, cache distribuído, revogação rápida, rate limiting e observabilidade."
sidebar_position: 6
tags: [dotnet, openiddict, security, pkce, redis, observability, opentelemetry, ratelimit]
---

# Temporada 6 — Hardening (Produção)

Agora que você tem:

- AuthServer (Identity + OpenIddict)
- Multi-tenant (Tenant + UserTenant)
- Permissões por tenant/app (Temporada 4)
- Isolamento de dados por tenant na API (Temporada 5)

… vamos colocar tudo no **nível produção**.

> **Importante:** nesta temporada existem decisões de produto. Para o tutorial, implemente o “mínimo produção” e deixe o resto como opcional.

---

# Checklist do “mínimo produção”

✅ Fluxo seguro para apps com usuário: **Authorization Code + PKCE**  
✅ Refresh token com políticas (expiração, revogação)  
✅ Cache distribuído de permissões (**Redis**)  
✅ Revogação rápida (invalidação de cache + checagens)  
✅ Rate limiting por tenant (API)  
✅ Logs com `TenantId` + correlação (`TraceId`)  
✅ Observabilidade com OpenTelemetry (API + AuthServer)

---

# Parte 1 — Trocar Password Flow → Authorization Code + PKCE

## 6.1 Por que?
Password flow é ruim para produção. Para apps web/mobile:

- Client não deve tocar a senha diretamente (ou guardar)
- PKCE protege contra code interception

## 6.2 Habilitar Authorization Code + PKCE no AuthServer

No `Program.cs` (OpenIddict Server), adicione:

```csharp
options.SetAuthorizationEndpointUris("/connect/authorize");
options.SetTokenEndpointUris("/connect/token");

// ✅ Produção: Authorization Code + PKCE
options.AllowAuthorizationCodeFlow()
       .RequireProofKeyForCodeExchange();

options.AllowRefreshTokenFlow();
```

> Para tutorial, você pode manter password flow só como legado, mas marque como “dev only”.

## 6.3 Configurar client para PKCE

No seed do client OpenIddict, adicione permissões:

```csharp
Permissions =
{
    OpenIddictConstants.Permissions.Endpoints.Authorization,
    OpenIddictConstants.Permissions.Endpoints.Token,
    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
    OpenIddictConstants.Permissions.ResponseTypes.Code,
    OpenIddictConstants.Permissions.Prefixes.Scope + "api"
}
```

E setar redirect URIs (exemplo):

```csharp
RedirectUris = { new Uri("https://localhost:4200/callback") },
PostLogoutRedirectUris = { new Uri("https://localhost:4200/") },
```

:::tip Checkpoint
Você consegue autenticar via auth code + PKCE.
:::

---

# Parte 2 — Certificados e rotação de chaves

## 6.4 Em produção, NÃO use certificados dev

No AuthServer, substitua:

```csharp
options.AddDevelopmentSigningCertificate();
options.AddDevelopmentEncryptionCertificate();
```

Por certificados reais (exemplo PFX):

```csharp
options.AddSigningCertificate(new X509Certificate2("signing.pfx", "senha"));
options.AddEncryptionCertificate(new X509Certificate2("encryption.pfx", "senha"));
```

## 6.5 Rotação
Estratégia simples:

- Mantenha o certificado antigo por um período (para validar tokens antigos)
- Adicione um novo para emissão
- Remova o antigo após expiração

:::tip Checkpoint
Tokens antigos continuam válidos durante rotação.
:::

---

# Parte 3 — Cache distribuído de permissões (Redis)

## 6.6 Redis (Docker)
```bash
docker run -d --name evoauth-redis -p 6379:6379 redis:7
```

## 6.7 Pacotes (API)
```bash
dotnet add src/EvoAuth.Api package Microsoft.Extensions.Caching.StackExchangeRedis
```

## 6.8 Registrar Redis Cache (API Program.cs)
```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "evoauth:";
});
```

## 6.9 Ajustar PermissionService para IDistributedCache
Troque `IMemoryCache` por `IDistributedCache`.

Cache key:
`perm:{userId}:{tenantId}:{appKey}`

TTL: 1–5 minutos (depende do seu produto).

Exemplo:

```csharp
var json = await cache.GetStringAsync(key, ct);
if (json is null)
{
    var perms = await FetchFromAuthServerAsync(...);
    await cache.SetStringAsync(
        key,
        JsonSerializer.Serialize(perms),
        new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        },
        ct);
}
```

:::tip Checkpoint
Requests repetidos não chamam AuthServer para permissões.
:::

---

# Parte 4 — Revogação rápida

## 6.10 Estratégia simples
Ao remover role/membership:
- Invalide cache (delete keys `perm:{userId}:{tenantId}:*`)
- (Opcional) versionamento por tenant

## 6.11 Cache busting por versão (recomendado)
No AuthServer:
- tabela `UserTenantSecurity (UserId, TenantId, Version)`
- ao alterar permissão/membership: incrementa `Version`
- endpoint de permissões retorna `Version`

A API usa `Version` no cache key:

`perm:{userId}:{tenantId}:{appKey}:v{version}`

:::tip Checkpoint
Revogação reflete rápido sem depender só de TTL.
:::

---

# Parte 5 — Rate limiting por tenant (API)

## 6.12 ASP.NET RateLimiter
No `Program.cs` da API:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("per-tenant", context =>
    {
        var tenantId = context.Request.Headers["X-Tenant-Id"].ToString();
        var key = string.IsNullOrWhiteSpace(tenantId) ? "no-tenant" : tenantId;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});
```

Pipeline:

```csharp
app.UseRateLimiter();
app.MapControllers().RequireRateLimiting("per-tenant");
```

:::tip Checkpoint
Cada tenant tem limite independente.
:::

---

# Parte 6 — Logs e Observabilidade (OpenTelemetry)

## 6.13 Enriquecer logs com TenantId
Middleware na API (exemplo):

```csharp
using System.Diagnostics;

using (logger.BeginScope(new Dictionary<string, object>
{
    ["TenantId"] = tenant.HasTenant ? tenant.TenantId : Guid.Empty,
    ["TraceId"] = Activity.Current?.TraceId.ToString() ?? ""
}))
{
    await _next(context);
}
```

## 6.14 OpenTelemetry (API + AuthServer)
Pacotes (API e AuthServer):

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Exporter.Console
```

Registro (exemplo simples):

```csharp
builder.Services.AddOpenTelemetry()
  .WithTracing(b =>
  {
      b.AddAspNetCoreInstrumentation();
      b.AddHttpClientInstrumentation();
      b.AddConsoleExporter();
  });
```

:::tip Checkpoint
Você enxerga traces e consegue correlacionar chamadas API ↔ AuthServer.
:::

---

# Parte 7 — Segurança adicional (recomendado)

- Exigir HTTPS sempre
- Validar audience/scope na API
- CORS bem definido (AuthServer)
- Lockout no Identity
- 2FA (opcional)

---

# Roteiro para o vídeo (Dev Cansado / técnico)

1) Identity → OpenIddict (token real)  
2) Multi-tenant (TenantResolver + /tenants/mine)  
3) Permissões (HasPermission + endpoint)  
4) Isolamento (QueryFilter + tenant injection)  
5) Hardening (PKCE + Redis + RateLimit + OTel)
