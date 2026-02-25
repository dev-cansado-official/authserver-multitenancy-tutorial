using EvoAuth.AuthServer.Data;
using EvoAuth.AuthServer.Entities;
using EvoAuth.AuthServer.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvoAuth.AuthServer.Controllers
{
    [ApiController]
    [Route("tenants/{tenantId:guid}")]
    [Authorize]
    public class RbacAdminController : ControllerBase
    {
        private readonly AuthDbContext _db;

        public RbacAdminController(AuthDbContext db) => _db = db;

        [HttpPost("roles")]
        public async Task<IActionResult> CreateRole(
            Guid tenantId,
            [FromBody] CreateRoleRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { error = "client_id_and_name_are_required" });

            var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == tenantId, ct);
            if (!tenantExists)
                return NotFound(new { error = "tenant_not_found" });

            var normalizedName = request.Name.Trim();
            var normalizedClientId = request.ClientId.Trim();

            var exists = await _db.RolesApp.AnyAsync(r =>
                r.TenantId == tenantId &&
                r.ClientId == normalizedClientId &&
                r.Name == normalizedName, ct);

            if (exists)
                return Conflict(new { error = "role_already_exists_for_tenant_and_client" });

            var role = new Role
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ClientId = normalizedClientId,
                Name = normalizedName
            };

            _db.RolesApp.Add(role);
            await _db.SaveChangesAsync(ct);

            return Created($"/tenants/{tenantId}/roles/{role.Id}", new
            {
                role.Id,
                role.TenantId,
                role.ClientId,
                role.Name
            });
        }

        [HttpGet("roles")]
        public async Task<IActionResult> ListRoles(
            Guid tenantId,
            [FromQuery] string clientId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return BadRequest(new { error = "client_id_is_required" });

            var roles = await _db.RolesApp
                .Where(r => r.TenantId == tenantId && r.ClientId == clientId)
                .OrderBy(r => r.Name)
                .Select(r => new
                {
                    r.Id,
                    r.TenantId,
                    r.ClientId,
                    r.Name
                })
                .ToListAsync(ct);

            return Ok(roles);
        }

        [HttpPost("roles/{roleId:guid}/permissions")]
        public async Task<IActionResult> AddRolePermissions(
            Guid tenantId,
            Guid roleId,
            [FromBody] AssignPermissionsRequest request,
            CancellationToken ct)
        {
            if (request.PermissionKeys is null || request.PermissionKeys.Count == 0)
                return BadRequest(new { error = "permission_keys_are_required" });

            var role = await _db.RolesApp
                .SingleOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, ct);

            if (role is null)
                return NotFound(new { error = "role_not_found_for_tenant" });

            var normalizedKeys = request.PermissionKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (normalizedKeys.Length == 0)
                return BadRequest(new { error = "permission_keys_are_required" });

            var permissions = await _db.Permissions
                .Where(p => normalizedKeys.Contains(p.Key))
                .ToListAsync(ct);

            var foundKeys = permissions.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            var missingKeys = normalizedKeys.Where(k => !foundKeys.Contains(k)).ToArray();
            if (missingKeys.Length > 0)
                return BadRequest(new { error = "unknown_permission_keys", missingKeys });

            var permissionIds = permissions.Select(p => p.Id).ToArray();
            var existingPermissionIds = await _db.RolePermissions
                .Where(rp => rp.RoleId == roleId && permissionIds.Contains(rp.PermissionId))
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);

            var existingSet = existingPermissionIds.ToHashSet();
            foreach (var permissionId in permissionIds.Where(id => !existingSet.Contains(id)))
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permissionId
                });
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                roleId,
                assignedPermissions = normalizedKeys
            });
        }

        [HttpPost("users/{userId:guid}/roles")]
        public async Task<IActionResult> AssignRoleToUser(
            Guid tenantId,
            Guid userId,
            [FromBody] AssignUserRoleRequest request,
            CancellationToken ct)
        {
            var role = await _db.RolesApp
                .SingleOrDefaultAsync(r => r.Id == request.RoleId && r.TenantId == tenantId, ct);

            if (role is null)
                return NotFound(new { error = "role_not_found_for_tenant" });

            var userExists = await _db.Users.AnyAsync(u => u.Id == userId, ct);
            if (!userExists)
                return NotFound(new { error = "user_not_found" });

            var isAlreadyAssigned = await _db.UserTenantRoles.AnyAsync(utr =>
                utr.UserId == userId &&
                utr.TenantId == tenantId &&
                utr.RoleId == request.RoleId, ct);

            if (!isAlreadyAssigned)
            {
                _db.UserTenantRoles.Add(new UserTenantRole
                {
                    UserId = userId,
                    TenantId = tenantId,
                    RoleId = request.RoleId
                });
            }

            var hasTenantMembership = await _db.UserTenants.AnyAsync(ut =>
                ut.UserId == userId && ut.TenantId == tenantId, ct);

            if (!hasTenantMembership)
            {
                _db.UserTenants.Add(new UserTenant
                {
                    UserId = userId,
                    TenantId = tenantId,
                    Status = "Active",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                tenantId,
                userId,
                roleId = request.RoleId
            });
        }
    }

    public sealed class CreateRoleRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public sealed class AssignPermissionsRequest
    {
        public List<string> PermissionKeys { get; set; } = new();
    }

    public sealed class AssignUserRoleRequest
    {
        public Guid RoleId { get; set; }
    }
}
