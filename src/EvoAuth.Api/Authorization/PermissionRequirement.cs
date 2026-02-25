using Microsoft.AspNetCore.Authorization;

namespace EvoAuth.Api.Authorization
{
    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permission) => Permission = permission;

        public string Permission { get; }
    }
}
