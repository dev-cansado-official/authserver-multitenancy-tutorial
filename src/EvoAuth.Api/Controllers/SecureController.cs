using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvoAuth.Api.Controllers
{
    [ApiController]
    [Route("secure")]
    public class SecureController : ControllerBase
    {
        [HttpGet("ping")]
        [Authorize(Policy = "scope:api")]
        public IActionResult Ping() => Ok(new { ok = true, user = User.Identity?.Name });
    }
}
