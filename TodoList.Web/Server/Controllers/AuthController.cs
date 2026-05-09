using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace TodoList.Web.Server.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    [HttpGet("google")]
    public IActionResult GoogleLogin([FromQuery] string returnUrl = "/")
    {
        var props = new AuthenticationProperties { RedirectUri = returnUrl };
        return Challenge(props, "Google");
    }

    [HttpGet("github")]
    public IActionResult GitHubLogin([FromQuery] string returnUrl = "/")
    {
        var props = new AuthenticationProperties { RedirectUri = returnUrl };
        return Challenge(props, "GitHub");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Cookies");
        return Redirect("/login");
    }

    /// <summary>
    /// Returns the current user's identity as seen by the Web.Server BFF.
    ///
    /// This endpoint is the sole owner of <c>/api/me</c> — the Api has an equivalent
    /// endpoint but it is only reachable server-to-server. The client always goes
    /// through Web.Server (same-origin with the Blazor WASM bundle) so auth cookies
    /// travel correctly. The shape matches what <c>UserProfileStrip</c> expects:
    /// <c>{UserId, Email, Name, AuthMethod}</c>.
    /// </summary>
    [HttpGet("/api/me")]
    public IActionResult Me()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return Unauthorized();

        var authMethod = User.FindFirstValue("auth_method")
                         ?? User.Identity!.AuthenticationType?.ToLowerInvariant()
                         ?? "cookie";

        return Ok(new
        {
            UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Email = User.FindFirstValue(ClaimTypes.Email),
            Name = User.FindFirstValue(ClaimTypes.Name),
            AuthMethod = authMethod
        });
    }
}
