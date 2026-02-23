using EvoAuth.AuthServer.Identity;
using EvoAuth.AuthServer.Tenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EvoAuth.AuthServer.Data
{
    public class AuthDbContext: IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<UserTenant> UserTenants => Set<UserTenant>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Registra as entidades do OpenIddict no mesmo banco
            builder.UseOpenIddict();


            builder.Entity<Tenant>(b => 
            {
                b.ToTable("Tenants");
                b.HasKey(x => x.Id);
                b.Property(x => x.Name).IsRequired().HasMaxLength(200);
                b.Property(x => x.Slug).IsRequired().HasMaxLength(200);
                b.HasIndex(x => x.Slug).IsUnique();
                b.Property(x => x.IsActive).IsRequired();
            });

            builder.Entity<UserTenant>(b =>
            {
                b.ToTable("UserTenants");
                b.HasKey(x => new { x.UserId, x.TenantId });

                b.Property(x => x.Status).IsRequired().HasMaxLength(50);
                b.Property(x => x.CreatedAtUtc).IsRequired();

                b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
                b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
            });
        }
    }
}
