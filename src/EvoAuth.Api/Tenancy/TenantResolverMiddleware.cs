using EvoAuth.Shared.Tenancy;

namespace EvoAuth.Api.Tenancy
{
    public sealed class TenantResolverMiddleware
    {
        private readonly RequestDelegate _next;
        public TenantResolverMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, TenantContext tenant)
        {
            if (!context.Request.Headers.TryGetValue(TenantHeaders.TenantId, out var value))
            {
                await _next(context);
                return;
            }

            if (!Guid.TryParse(value.ToString(), out var tenantId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "invalid_tenant_id" });
                return;
            }

            tenant.TenantId = tenantId;
            tenant.HasTenant = true;

            await _next(context);
        }
    }
}
