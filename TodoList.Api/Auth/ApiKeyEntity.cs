// TodoList.Api/Auth/ApiKeyEntity.cs
namespace TodoList.Api.Auth;

/// <summary>
/// Pre-shared API key for agent / MCP server access.
/// KeyHash is SHA-256(key) stored as hex — the plain-text key is never persisted.
/// </summary>
public class ApiKeyEntity
{
    public Guid Id { get; set; }
    public string KeyHash { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "write"; // read | write | admin
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
}
