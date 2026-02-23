using EvoAuth.AuthServer.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EvoAuth.AuthServer.Data
{
    public class AuthDbContext: IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Registra as entidades do OpenIddict no mesmo banco
            builder.UseOpenIddict();
        }
    }
}
