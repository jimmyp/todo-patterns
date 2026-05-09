// TodoList.Api/Auth/AppUser.cs
using Microsoft.AspNetCore.Identity;

namespace TodoList.Api.Auth;

/// <summary>
/// Application user — extends IdentityUser with no extra fields for now.
/// The user's Id (a GUID string) is what we store as UserId on todos and categories.
/// </summary>
public class AppUser : IdentityUser
{
}
