using EvoAuth.AuthServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvoAuth.AuthServer.Controllers
{
    [ApiController]
    [Route("tenants")]
    [Authorize]
    public class TenantsController : ControllerBase
    {
        private readonly AuthDbContext _db;

        public TenantsController(AuthDbContext db) => _db = db;

    [HttpGet("mine")]
        public async Task<IActionResult> Mine()
        {
            var sub = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var userId))
                return Unauthorized();

            var tenants = await _db.UserTenants
                .Where(x => x.UserId == userId && x.Status == "Active")
            .Select(x => new { x.Tenant.Id, x.Tenant.Name, x.Tenant.Slug, x.Tenant.IsActive })
            .ToListAsync();

            return Ok(tenants);
        }
    }
}
