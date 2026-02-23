namespace EvoAuth.AuthServer.Tenancy
{
    public class Tenant
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string Slug { get; set; } = default!;
        public bool IsActive { get; set; } = true;
    }
}
