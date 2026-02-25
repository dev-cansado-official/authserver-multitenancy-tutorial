using System.Security.Claims;

namespace EvoAuth.Api.Authorization
{
    public interface IPermissionService
    {
        Task<bool> HasPermissionAsync(
            ClaimsPrincipal user,
            Guid tenantId,
            string requiredPermission,
            string accessToken,
            CancellationToken ct);
    }
}
