using EvoAuth.AuthServer.Identity;

namespace EvoAuth.AuthServer.Tenancy
{
    public class UserTenant
    {
        public Guid UserId { get; set; }
        public ApplicationUser User { get; set; } = default!;

        public Guid TenantId { get; set; }
        public Tenant Tenant { get; set; } = default!;

        public string Status { get; set; } = "Active";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
