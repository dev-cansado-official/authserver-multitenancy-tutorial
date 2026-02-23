using EvoAuth.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EvoAuth.Api.Controllers
{
    [ApiController]
    [Route("tenant")]
    public class TenantController : ControllerBase
    {
        [HttpGet("current")]
        [Authorize]
        public IActionResult Current([FromServices] ITenantContext tenant)
        {
            if (!tenant.HasTenant)
                return BadRequest(new { error = "missing_tenant_header" });

            return Ok(new { tenantId = tenant.TenantId });
        }
    }
}
