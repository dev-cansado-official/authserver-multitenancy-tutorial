namespace EvoAuth.AuthServer.Entities
{
    public class UserTenantRole
    {
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }

        public Guid RoleId { get; set; }
        public Role Role { get; set; } = default!;
    }
}
