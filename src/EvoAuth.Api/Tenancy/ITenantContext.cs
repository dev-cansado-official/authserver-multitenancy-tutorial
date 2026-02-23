namespace EvoAuth.Api.Tenancy
{
    public interface ITenantContext
    {
        Guid TenantId { get; }
        bool HasTenant { get; }
    }
}
