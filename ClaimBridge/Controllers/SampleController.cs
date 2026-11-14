using System.Security.Claims;
using ClaimBridge.Common;
using ClaimBridge.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimBridge.Controllers;

[ApiController]
[Route("api/sample")]
[Authorize]
public sealed class SampleController : ControllerBase
{
    [HttpGet("protected")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> Protected() =>
        Ok(new
        {
            message = "You are authenticated.",
            userId = User.FindFirstValue(ClaimNames.Sub),
            userName = User.Identity?.Name
        });

    [HttpGet("admin")]
    [Authorize(Roles = RoleSeeder.AdminRole)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<object> Admin() =>
        Ok(new { message = "You are an administrator." });
}
