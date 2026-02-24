namespace EvoAuth.AuthServer.Entities
{
    public class Role
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }

        // ✅ aplicação = client OAuth (OpenIddictApplication.ClientId)
        public string ClientId { get; set; } = default!;

        public string Name { get; set; } = default!; // ex: "GlobalAdmin", "Viewer"
    }
}
