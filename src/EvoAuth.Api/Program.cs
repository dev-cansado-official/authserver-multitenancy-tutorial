using EvoAuth.Api.Tenancy;
using EvoAuth.Shared.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    });

var authority = builder.Configuration["Auth:Authority"];

builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(authority);
        options.AddAudiences(AuthConstants.ApiResource);
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("scope:api", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "api");
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolverMiddleware>();

app.MapControllers();

app.Run();