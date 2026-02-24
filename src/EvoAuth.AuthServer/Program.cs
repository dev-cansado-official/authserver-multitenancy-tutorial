using EvoAuth.AuthServer.Data;
using EvoAuth.AuthServer.Entities;
using EvoAuth.AuthServer.Identity;
using EvoAuth.AuthServer.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("AuthServerDb"));
    options.UseOpenIddict();
});

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 6;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<AuthDbContext>();
    })
    .AddServer(options =>
    {
        options.SetIssuer("https://localhost:7155/");

        // Endpoints
        options.SetTokenEndpointUris("/connect/token");
        
        // Flows (tutorial)
        options.AllowPasswordFlow();
        options.AllowRefreshTokenFlow();

        // Scopes
        options.RegisterScopes("api");

        // Certificados dev
        //options.AddDevelopmentEncryptionCertificate()
        //       .AddDevelopmentSigningCertificate();
        options.AddDevelopmentSigningCertificate();
        options.AddEphemeralEncryptionKey();

        options.DisableAccessTokenEncryption();

        // ASP.NET Core host
        options.UseAspNetCore()
               .EnableTokenEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        // Validação local no AuthServer (útil para endpoints do próprio AuthServer)
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    //app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    await SeedAsync(scope.ServiceProvider);
}


app.Run();


static async Task SeedAsync(IServiceProvider sp)
{
    var db = sp.GetRequiredService<AuthDbContext>();
    var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
    var scopeManager = sp.GetRequiredService<IOpenIddictScopeManager>();
    var appManager = sp.GetRequiredService<IOpenIddictApplicationManager>();

    // Usuário demo
    var demoEmail = "demo@local";
    var user = await userManager.FindByEmailAsync(demoEmail);
    if (user is null)
    {
        user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = demoEmail,
            Email = demoEmail,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, "Demo123!");
        if (!result.Succeeded)
            throw new Exception(string.Join(" | ", result.Errors.Select(e => e.Description)));
    }

    // Client do tutorial
    if (await appManager.FindByClientIdAsync("evo-api-client") is null)
    {
        await appManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = "evo-api-client",
            ClientSecret = "evo-secret",
            DisplayName = "Evo API Client",

            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.Password,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Prefixes.Scope + "api"

            }
        });
    }

    // Dogfooding (temporada 4 tópico 6)
    const string internalTenantSlug = "internal";
    const string adminPortalClientId = "opentenid-admin-portal";
    const string adminPortalRoleName = "GlobalAdmin";

    var adminPortalPermissionCatalog = new Dictionary<string, string>
    {
        ["tenants.create"] = "Create tenants",
        ["tenants.read"] = "Read tenants",
        ["tenants.update"] = "Update tenants",
        ["users.invite"] = "Invite users",
        ["users.read"] = "Read users",
        ["users.disable"] = "Disable users",
        ["roles.create"] = "Create roles",
        ["roles.assign"] = "Assign roles",
        ["permissions.manage"] = "Manage permissions",
        ["clients.manage"] = "Manage OAuth clients"
    };

    var internalTenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == internalTenantSlug);
    if (internalTenant is null)
    {
        internalTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Internal",
            Slug = internalTenantSlug,
            IsActive = true
        };

        db.Tenants.Add(internalTenant);
        await db.SaveChangesAsync();
    }

    if (await appManager.FindByClientIdAsync(adminPortalClientId) is null)
    {
        await appManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = adminPortalClientId,
            ClientSecret = "opentenid-admin-portal-secret",
            DisplayName = "OpenTenId Admin Portal",

            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.Password,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Prefixes.Scope + "api"
            }
        });
    }

    var permissionKeys = adminPortalPermissionCatalog.Keys.ToArray();
    var existingPermissionKeys = await db.Permissions
        .Where(p => permissionKeys.Contains(p.Key))
        .Select(p => p.Key)
        .ToListAsync();

    var missingPermissionKeys = permissionKeys.Except(existingPermissionKeys);
    foreach (var key in missingPermissionKeys)
    {
        db.Permissions.Add(new Permission
        {
            Id = Guid.NewGuid(),
            Key = key,
            Description = adminPortalPermissionCatalog[key]
        });
    }

    if (missingPermissionKeys.Any())
        await db.SaveChangesAsync();

    var globalAdminRole = await db.RolesApp.SingleOrDefaultAsync(r =>
        r.TenantId == internalTenant.Id &&
        r.ClientId == adminPortalClientId &&
        r.Name == adminPortalRoleName);

    if (globalAdminRole is null)
    {
        globalAdminRole = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = internalTenant.Id,
            ClientId = adminPortalClientId,
            Name = adminPortalRoleName
        };

        db.RolesApp.Add(globalAdminRole);
        await db.SaveChangesAsync();
    }

    var adminPermissionIds = await db.Permissions
        .Where(p => permissionKeys.Contains(p.Key))
        .Select(p => p.Id)
        .ToListAsync();

    var linkedPermissionIds = await db.RolePermissions
        .Where(rp => rp.RoleId == globalAdminRole.Id)
        .Select(rp => rp.PermissionId)
        .ToListAsync();

    foreach (var permissionId in adminPermissionIds.Except(linkedPermissionIds))
    {
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = globalAdminRole.Id,
            PermissionId = permissionId
        });
    }

    if (!await db.UserTenants.AnyAsync(ut => ut.UserId == user.Id && ut.TenantId == internalTenant.Id))
    {
        db.UserTenants.Add(new UserTenant
        {
            UserId = user.Id,
            TenantId = internalTenant.Id,
            Status = "Active",
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    if (!await db.UserTenantRoles.AnyAsync(utr =>
        utr.UserId == user.Id &&
        utr.TenantId == internalTenant.Id &&
        utr.RoleId == globalAdminRole.Id))
    {
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserId = user.Id,
            TenantId = internalTenant.Id,
            RoleId = globalAdminRole.Id
        });
    }

    await db.SaveChangesAsync();

    // Scope "api"
    if (await scopeManager.FindByNameAsync("api") is null)
    {
        await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
        {
            Name = "api",
            DisplayName = "Evo API",
            Resources = { "evo-api" }
        });
    }
}
