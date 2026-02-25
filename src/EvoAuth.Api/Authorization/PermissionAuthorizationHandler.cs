using EvoAuth.Shared.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace EvoAuth.Api.Authorization
{
    public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IPermissionService _permissionService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public PermissionAuthorizationHandler(
            IPermissionService permissionService,
            IHttpContextAccessor httpContextAccessor)
        {
            _permissionService = permissionService;
            _httpContextAccessor = httpContextAccessor;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            var httpContext = context.Resource as HttpContext ?? _httpContextAccessor.HttpContext;
            if (httpContext is null)
                return;

            if (!httpContext.Request.Headers.TryGetValue(TenantHeaders.TenantId, out var tenantValue))
                return;

            if (!Guid.TryParse(tenantValue.ToString(), out var tenantId))
                return;

            if (!httpContext.Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
                return;

            var token = ParseBearerToken(authorizationHeader.ToString());
            if (string.IsNullOrWhiteSpace(token))
                return;

            var hasPermission = await _permissionService.HasPermissionAsync(
                context.User,
                tenantId,
                requirement.Permission,
                token,
                httpContext.RequestAborted);

            if (hasPermission)
                context.Succeed(requirement);
        }

        private static string? ParseBearerToken(string headerValue)
        {
            const string bearerPrefix = "Bearer ";
            return headerValue.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? headerValue[bearerPrefix.Length..].Trim()
                : null;
        }
    }
}
