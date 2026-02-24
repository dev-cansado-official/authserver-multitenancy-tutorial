namespace EvoAuth.AuthServer.Entities
{
    public class Permission
    {
        public Guid Id { get; set; }
        public string Key { get; set; } = default!; // ex: "orders.read"
        public string Description { get; set; } = default!;
    }
}
