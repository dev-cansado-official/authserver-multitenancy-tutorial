using EvoAuth.AuthServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvoAuth.AuthServer.Controllers
{
    [ApiController]
    [Route("tenants")]
    [Authorize]
    public class PermissionsController : ControllerBase
    {
        private readonly AuthDbContext _db;
        public PermissionsController(AuthDbContext db) => _db = db;

        [HttpGet("{tenantId:guid}/me/permissions")]
        public async Task<IActionResult> GetPermissions(Guid tenantId, CancellationToken ct)
        {
            var sub = User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(sub, out var userId))
                return Unauthorized();

            var clientId = User.FindFirst("client_id")?.Value;
            if (string.IsNullOrWhiteSpace(clientId))
                return Forbid();

            var permissions = await _db.UserTenantRoles
                .Where(utr => utr.UserId == userId && utr.TenantId == tenantId)
                .Join(_db.RolesApp,
                    utr => utr.RoleId,
                    r => r.Id,
                    (utr, r) => new { r.Id, r.ClientId })
                .Where(x => x.ClientId == clientId)
                .Join(_db.RolePermissions,
                    x => x.Id,
                    rp => rp.RoleId,
                    (x, rp) => rp.PermissionId)
                .Join(_db.Permissions,
                    pid => pid,
                    p => p.Id,
                    (pid, p) => p.Key)
                .Distinct()
                .ToListAsync(ct);

            return Ok(new
            {
                tenantId,
                clientId,
                permissions
            });
        }
    }
}
