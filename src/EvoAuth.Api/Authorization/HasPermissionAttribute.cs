using Microsoft.AspNetCore.Authorization;

namespace EvoAuth.Api.Authorization
{
    public sealed class HasPermissionAttribute : AuthorizeAttribute
    {
        public const string PolicyPrefix = "perm:";

        public HasPermissionAttribute(string permission)
        {
            Policy = $"{PolicyPrefix}{permission}";
        }
    }
}
