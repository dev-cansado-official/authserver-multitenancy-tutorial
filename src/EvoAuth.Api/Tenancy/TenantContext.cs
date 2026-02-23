namespace EvoAuth.Api.Tenancy
{
    public sealed class TenantContext : ITenantContext
    {
        public Guid TenantId { get; internal set; }
        public bool HasTenant { get; internal set; }
    }
}
