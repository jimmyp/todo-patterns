// TodoList.Api/Auth/ApiKeyAuthHandler.cs
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace TodoList.Api.Auth;

public class ApiKeyAuthOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Validates "Authorization: Bearer {api-key}" requests.
/// Looks up the SHA-256 hash of the key in the ApiKeys table.
/// On success, creates a ClaimsPrincipal with the key owner's UserId.
/// </summary>
public class ApiKeyAuthHandler(
    IOptionsMonitor<ApiKeyAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDbContextFactory<AppIdentityDbContext> dbFactory)
    : AuthenticationHandler<ApiKeyAuthOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var rawKey = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(rawKey))
            return AuthenticateResult.Fail("Empty API key");

        var hash = ComputeHash(rawKey);

        await using var db = await dbFactory.CreateDbContextAsync();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k =>
            k.KeyHash == hash &&
            !k.IsRevoked &&
            (k.ExpiresAt == null || k.ExpiresAt > DateTimeOffset.UtcNow));

        if (key is null)
            return AuthenticateResult.Fail("Invalid or expired API key");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, key.UserId),
            new Claim(ClaimTypes.Role, key.Role),
            new Claim("auth_method", "api_key")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    internal static string ComputeHash(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
