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

    [HttpGet("/api/me")]
    public IActionResult Me()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return Unauthorized();

        return Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            name = User.FindFirstValue(ClaimTypes.Name),
            email = User.FindFirstValue(ClaimTypes.Email),
            avatarUrl = User.FindFirstValue("picture")
        });
    }
}
