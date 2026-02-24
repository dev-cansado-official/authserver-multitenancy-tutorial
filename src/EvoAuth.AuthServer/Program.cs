using EvoAuth.AuthServer.Data;
using EvoAuth.AuthServer.Identity;
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