---
description: Implementa TenantId em entidades tenant-owned, QueryFilter
  global, validações e proteção contra vazamento entre tenants.
id: evoauth-temporada-5
sidebar_position: 5
tags:
- dotnet
- multitenant
- efcore
- sqlserver
- isolation
- security
title: Temporada 5 --- Isolamento de Dados por Tenant (EF Core +
  QueryFilter)
---

# Temporada 5 --- Isolamento de Dados por Tenant

Nesta temporada você vai garantir **isolamento real de dados** na
`EvoAuth.Api`:

-   Toda entidade "tenant-owned" terá `TenantId`
-   O `DbContext` aplica **QueryFilter global** com base no
    `ITenantContext`
-   Proteções contra bypass (ex: criação/edição com TenantId errado)
-   Checkpoints e testes rápidos com Postman/Swagger

> Pré-requisito: Temporada 3 (TenantResolver com `X-Tenant-Id`) já
> implementada na API.

------------------------------------------------------------------------

## Conceito: "Tenant-owned data"

Tudo que pertence a uma empresa/tenant (ex: pedidos, clientes,
agendamentos) **precisa** carregar `TenantId`.

``` mermaid
erDiagram
  TENANT ||--o{ ORDER : owns
  ORDER {
    uniqueidentifier Id
    uniqueidentifier TenantId
    decimal Total
  }
```

------------------------------------------------------------------------

# Parte 1 --- Base: contratos e interface de "Tenant-owned"

## 5.1 Criar interface `ITenantOwnedEntity` (API)

Crie em `src/EvoAuth.Api/Multitenancy`:

### `src/EvoAuth.Api/Multitenancy/ITenantOwnedEntity.cs`

``` csharp
namespace EvoAuth.Api.Multitenancy;

public interface ITenantOwnedEntity
{
    Guid TenantId { get; set; }
}
```

> Isso te permite aplicar regras genéricas de proteção.

------------------------------------------------------------------------

# Parte 2 --- Criar um modelo real na API (exemplo: Orders)

## 5.2 Criar entidade `Order`

Crie em `src/EvoAuth.Api/Domain`:

### `src/EvoAuth.Api/Domain/Order.cs`

``` csharp
using EvoAuth.Api.Multitenancy;

namespace EvoAuth.Api.Domain;

public class Order : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string Number { get; set; } = default!;
    public decimal Total { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
```

------------------------------------------------------------------------

# Parte 3 --- DbContext da API + QueryFilter global

## 5.3 Pacotes (API)

Se a API ainda não tem EF Core:

``` bash
dotnet add src/EvoAuth.Api package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/EvoAuth.Api package Microsoft.EntityFrameworkCore.Tools
```

## 5.4 Connection String (API)

Em `src/EvoAuth.Api/appsettings.json`:

``` json
{
  "ConnectionStrings": {
    "ApiDb": "Server=localhost,1433;Database=EvoAuth_Api;User Id=sa;Password=Your_password123;TrustServerCertificate=True"
  }
}
```

## 5.5 Criar `ApiDbContext`

Crie em `src/EvoAuth.Api/Data`:

### `src/EvoAuth.Api/Data/ApiDbContext.cs`

``` csharp
using EvoAuth.Api.Domain;
using EvoAuth.Api.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EvoAuth.Api.Data;

public class ApiDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public ApiDbContext(DbContextOptions<ApiDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("Orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).IsRequired().HasMaxLength(30);
            b.Property(x => x.Total).HasPrecision(18, 2);
            b.Property(x => x.CreatedAtUtc).IsRequired();

            // ✅ QueryFilter global: SEMPRE restringe pelo TenantId atual
            b.HasQueryFilter(o => !_tenant.HasTenant || o.TenantId == _tenant.TenantId);

            // Índice para performance (quase obrigatório)
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique(false);
        });
    }
}
```

> Observação importante:\
> Eu coloquei `!_tenant.HasTenant || ...` para não quebrar endpoints que
> não exigem tenant.\
> Se você quer "tenant obrigatório em tudo", troque por:
> `o.TenantId == _tenant.TenantId`.

------------------------------------------------------------------------

## 5.6 Registrar DbContext no Program.cs (API)

No `src/EvoAuth.Api/Program.cs`:

``` csharp
using EvoAuth.Api.Data;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<ApiDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("ApiDb"));
});
```

------------------------------------------------------------------------

## 5.7 Migrations (API)

``` bash
dotnet ef migrations add InitApiDb -o Data/Migrations --project src/EvoAuth.Api --startup-project src/EvoAuth.Api
dotnet ef database update --project src/EvoAuth.Api --startup-project src/EvoAuth.Api
```

:::tip Checkpoint A tabela `Orders` foi criada no banco `EvoAuth_Api`.
:::

------------------------------------------------------------------------

# Parte 4 --- Proteção contra "TenantId injection"

QueryFilter te protege em leituras, mas ainda precisa garantir:

-   Ninguém consegue **criar** `Order` com `TenantId` de outro tenant
-   Ninguém consegue **editar** e trocar `TenantId`

## 5.8 Interceptor simples: setar TenantId automaticamente (API)

Crie em `src/EvoAuth.Api/Multitenancy`:

### `src/EvoAuth.Api/Multitenancy/TenantIdSaveChangesInterceptor.cs`

``` csharp
using EvoAuth.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using EvoAuth.Api.Multitenancy;

namespace EvoAuth.Api.Multitenancy;

public static class TenantIdEnforcer
{
    public static void EnforceTenantId(DbContext db, ITenantContext tenant)
    {
        if (!tenant.HasTenant)
            return;

        foreach (var entry in db.ChangeTracker.Entries<ITenantOwnedEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                // ✅ sempre força o TenantId do contexto
                entry.Entity.TenantId = tenant.TenantId;
            }

            if (entry.State == EntityState.Modified)
            {
                // ✅ bloqueia troca de TenantId
                var original = (Guid)entry.OriginalValues[nameof(ITenantOwnedEntity.TenantId)]!;
                if (original != tenant.TenantId || entry.Entity.TenantId != tenant.TenantId)
                {
                    throw new InvalidOperationException("TenantId change is not allowed.");
                }
            }
        }
    }
}
```

Agora chame isso no `ApiDbContext`:

### `ApiDbContext.cs` (adicionar override)

``` csharp
using EvoAuth.Api.Multitenancy;

public override int SaveChanges()
{
    TenantIdEnforcer.EnforceTenantId(this, _tenant);
    return base.SaveChanges();
}

public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    TenantIdEnforcer.EnforceTenantId(this, _tenant);
    return base.SaveChangesAsync(cancellationToken);
}
```

:::tip Checkpoint Criar/editar sempre respeita o TenantId do header,
mesmo que alguém tente "forçar" no JSON. :::

------------------------------------------------------------------------

# Parte 5 --- Controller de exemplo (Orders) para testar o isolamento

Crie em `src/EvoAuth.Api/Controllers`:

### `src/EvoAuth.Api/Controllers/OrdersController.cs`

``` csharp
using EvoAuth.Api.Data;
using EvoAuth.Api.Domain;
using EvoAuth.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvoAuth.Api.Controllers;

[ApiController]
[Route("orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly ApiDbContext _db;
    private readonly ITenantContext _tenant;

    public OrdersController(ApiDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest req)
    {
        if (!_tenant.HasTenant)
            return BadRequest(new { error = "missing_tenant_header" });

        var order = new Order
        {
            Id = Guid.NewGuid(),
            // TenantId será forçado no SaveChanges
            Number = req.Number,
            Total = req.Total
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, new
        {
            order.Id,
            order.Number,
            order.Total,
            TenantId = order.TenantId
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        if (!_tenant.HasTenant)
            return BadRequest(new { error = "missing_tenant_header" });

        // ✅ QueryFilter garante que só acha se for do tenant atual
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();

        return Ok(order);
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!_tenant.HasTenant)
            return BadRequest(new { error = "missing_tenant_header" });

        var orders = await _db.Orders
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.Id, x.Number, x.Total, x.CreatedAtUtc })
            .ToListAsync();

        return Ok(orders);
    }

    public record CreateOrderRequest(string Number, decimal Total);
}
```

------------------------------------------------------------------------

# Parte 6 --- Teste de isolamento (passo a passo)

## 6.1 Crie 2 tenants no AuthServer

Use o seed da Temporada 3 (ex: `acme` e `umbrella`), pegue os
`TenantId`.

## 6.2 Obtenha um access_token

`POST /connect/token` (password flow), pegue o `access_token`.

## 6.3 Crie 1 pedido no Tenant A

Requisição:

-   `POST /orders`
-   Headers:
    -   `Authorization: Bearer <token>`
    -   `X-Tenant-Id: <TenantAId>`
-   Body:

``` json
{ "number": "A-001", "total": 10.50 }
```

Guarde o `orderId` retornado.

## 6.4 Tente buscar o mesmo pedido no Tenant B (deve falhar)

-   `GET /orders/{orderId}`
-   Headers:
    -   `Authorization: Bearer <token>`
    -   `X-Tenant-Id: <TenantBId>`

**Resultado esperado:** `404 Not Found` (porque o QueryFilter esconde
dados do outro tenant).

:::tip Checkpoint Você comprovou isolamento de leitura. :::

## 6.5 Tente criar um pedido "forçando TenantId" no JSON (deve ignorar)

Envie:

``` json
{ "tenantId": "GUID-DO-OUTRO-TENANT", "number": "HACK-1", "total": 1 }
```

Mesmo que seu DTO permita, o `SaveChanges` vai forçar o TenantId
correto.

:::tip Checkpoint Você comprovou proteção contra "tenant injection". :::

------------------------------------------------------------------------

# Boas práticas (produção)

-   Sempre indexe por `TenantId` (quase todas queries usam isso)
-   Evite expor `TenantId` em DTO de criação/edição
-   Faça validação de membership (AuthServer) quando necessário
    (Temporada 4 já cobre permissão; membership você pode checar na API
    se quiser)
-   Para endpoints públicos, decida se `TenantId` é obrigatório ou não
    (e padronize)

------------------------------------------------------------------------

# Próximo passo

**Temporada 6 --- Hardening**: - Cache distribuído (Redis) para
permissões - Invalidação de cache (revogação rápida) - Rate limiting por
tenant - Observabilidade com `TenantId` em logs/traces - Migrar password
flow → Authorization Code + PKCE (melhor prática)
