using EvoAuth.Api.Authorization;
using EvoAuth.Api.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace EvoAuth.Api.Controllers
{
    [ApiController]
    [Route("tenant")]
    public class TenantController : ControllerBase
    {
        [HttpGet("current")]
        [HasPermission("tenants.read")]
        public IActionResult Current([FromServices] ITenantContext tenant)
        {
            if (!tenant.HasTenant)
                return BadRequest(new { error = "missing_tenant_header" });

            return Ok(new { tenantId = tenant.TenantId });
        }
    }
}
