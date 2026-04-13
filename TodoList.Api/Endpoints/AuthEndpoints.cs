// TodoList.Api/Endpoints/AuthEndpoints.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TodoList.Api.Auth;

namespace TodoList.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/me — returns current user identity
        app.MapGet("/api/me", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = ctx.User.FindFirst(ClaimTypes.Email)?.Value;
            var name = ctx.User.FindFirst(ClaimTypes.Name)?.Value
                       ?? ctx.User.FindFirst("name")?.Value;
            var authMethod = ctx.User.FindFirst("auth_method")?.Value ?? "cookie";

            return Results.Ok(new
            {
                UserId = userId,
                Email = email,
                Name = name,
                AuthMethod = authMethod
            });
        }).RequireAuthorization();

        // POST /api/auth/logout
        app.MapPost("/api/auth/logout", async (HttpContext ctx, SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok();
        }).RequireAuthorization();
    }
}
