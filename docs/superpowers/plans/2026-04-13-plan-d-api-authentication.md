# Plan D: API Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ASP.NET Core Identity + Google/GitHub OAuth to the API with user-scoped data and a `/api/me` endpoint, while keeping all existing integration tests passing.

**Architecture:** ASP.NET Core Identity manages local user records in a separate `AppIdentityDbContext` (same SQL Server database, separate EF context). Google and GitHub OAuth handle credential storage. API endpoints are protected with cookie auth for browser sessions and API key (bearer token) for agents/MCP servers. Integration tests bypass auth via a test-scoped claim injection — no test credentials needed. The `anonymous` userId already used throughout the codebase becomes the authenticated user's Identity sub claim.

**Tech Stack:** .NET 10, ASP.NET Core Identity 10, AspNet.Security.OAuth.GitHub, Microsoft.AspNetCore.Authentication.Google, EF Core 10 (identity migrations), Microsoft.AspNetCore.Authentication.JwtBearer

> **Read before starting:** `TodoList.Api/Program.cs`, `TodoList.Api/Data/TodoDbContext.cs`, `TodoList.Api/Endpoints/TodoEndpoints.cs`, `TodoList.Api/TodoList.Api.csproj`, `TodoList.IntegrationTests/Fixtures/ApiFixture.cs`, `TodoList.IntegrationTests/GlobalUsings.cs`

---

## File Map

### New: `TodoList.Api/`
```
TodoList.Api/Auth/AppIdentityDbContext.cs          # Identity EF context (separate from TodoDbContext)
TodoList.Api/Auth/AppUser.cs                       # IdentityUser subclass
TodoList.Api/Auth/ApiKeyAuthHandler.cs             # Bearer token → API key validation handler
TodoList.Api/Auth/ApiKeyEntity.cs                  # EF entity for ApiKeys table
TodoList.Api/Endpoints/AuthEndpoints.cs            # /api/me, /api/auth/logout
TodoList.Api/Migrations/Identity/                  # Identity schema migrations (separate migration assembly)
```

### Modified: `TodoList.Api/`
```
TodoList.Api/Program.cs                            # add Identity, Google, GitHub, ApiKey auth
TodoList.Api/TodoList.Api.csproj                   # add Identity + OAuth packages
```

### Modified: `TodoList.IntegrationTests/`
```
TodoList.IntegrationTests/Fixtures/ApiFixture.cs   # inject test user claim so all endpoints work
TodoList.IntegrationTests/Auth/MeEndpointTests.cs  # new — test /api/me
```

---

## Tasks

### Task 1: Add packages

- [ ] **Step 1: Add NuGet packages to TodoList.Api**

```bash
cd /Users/jim/code/todo-patterns
dotnet add TodoList.Api/TodoList.Api.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 10.0.5
dotnet add TodoList.Api/TodoList.Api.csproj package AspNet.Security.OAuth.GitHub --version 9.0.0
dotnet add TodoList.Api/TodoList.Api.csproj package Microsoft.AspNetCore.Authentication.Google --version 10.0.5
```

Expected: each command prints "PackageReference ... added" with no errors.

- [ ] **Step 2: Verify csproj has the new packages**

Open `TodoList.Api/TodoList.Api.csproj` — should now include:
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
- `AspNet.Security.OAuth.GitHub`
- `Microsoft.AspNetCore.Authentication.Google`

- [ ] **Step 3: Build to confirm packages restore**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Api/TodoList.Api.csproj
```

Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Api/TodoList.Api.csproj
git commit -m "feat(auth): add Identity + OAuth NuGet packages to Api"
```

---

### Task 2: Add Identity entities and DbContext

- [ ] **Step 1: Create `TodoList.Api/Auth/AppUser.cs`**

```csharp
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
```

- [ ] **Step 2: Create `TodoList.Api/Auth/ApiKeyEntity.cs`**

```csharp
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
```

- [ ] **Step 3: Create `TodoList.Api/Auth/AppIdentityDbContext.cs`**

```csharp
// TodoList.Api/Auth/AppIdentityDbContext.cs
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.Auth;

/// <summary>
/// Separate EF context for ASP.NET Core Identity tables + ApiKeys.
/// Shares the same SQL Server database as TodoDbContext but keeps identity
/// schema separate so migrations can be managed independently.
/// </summary>
public class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApiKeyEntity>(b =>
        {
            b.HasKey(k => k.Id);
            b.HasIndex(k => k.KeyHash).IsUnique();
            b.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();
            b.Property(k => k.UserId).HasMaxLength(450).IsRequired();
            b.Property(k => k.Role).HasMaxLength(20).IsRequired();
        });
    }
}
```

- [ ] **Step 4: Build to confirm no compile errors**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Api/TodoList.Api.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Api/Auth/
git commit -m "feat(auth): add AppUser, ApiKeyEntity, AppIdentityDbContext"
```

---

### Task 3: Add API key authentication handler

- [ ] **Step 1: Create `TodoList.Api/Auth/ApiKeyAuthHandler.cs`**

```csharp
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
```

- [ ] **Step 2: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Api/TodoList.Api.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Api/Auth/ApiKeyAuthHandler.cs
git commit -m "feat(auth): add ApiKey bearer token authentication handler"
```

---

### Task 4: Add /api/me endpoint

- [ ] **Step 1: Create `TodoList.Api/Endpoints/AuthEndpoints.cs`**

```csharp
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
```

- [ ] **Step 2: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Api/TodoList.Api.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Api/Endpoints/AuthEndpoints.cs
git commit -m "feat(auth): add /api/me and /api/auth/logout endpoints"
```

---

### Task 5: Wire up auth in Program.cs

- [ ] **Step 1: Replace `TodoList.Api/Program.cs`**

```csharp
// TodoList.Api/Program.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TodoList.Api.Auth;
using TodoList.Api.Data;
using TodoList.Api.Endpoints;
using TodoList.Api.EventHandlers;
using TodoList.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerDbContext<TodoDbContext>("todolist", settings =>
{
    settings.DisableHealthChecks = true;
});

// Identity DbContext — same connection string, separate EF context
var connStr = builder.Configuration.GetConnectionString("todolist");
builder.Services.AddDbContextFactory<AppIdentityDbContext>(options =>
    options.UseSqlServer(connStr));
builder.Services.AddDbContext<AppIdentityDbContext>(options =>
    options.UseSqlServer(connStr));

builder.Services.AddScoped<ITodoRepository, TodoRepository>();
builder.Services.AddScoped<IOperationRepository, OperationRepository>();
builder.Services.AddScoped<ICategoryListRepository, CategoryListRepository>();
builder.Services.AddScoped<TodoProjectionHandler>();
builder.Services.AddScoped<CategoryProjectionHandler>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TodoDbContext>(tags: ["ready"]);

builder.Services.AddSignalR();
builder.Services.AddOpenApi();

// ASP.NET Core Identity
builder.Services
    .AddIdentity<AppUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppIdentityDbContext>()
    .AddDefaultTokenProviders();

// Cookie configuration (browser sessions from Web project)
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.Events.OnRedirectToLogin = ctx =>
    {
        // For API endpoints, return 401 instead of redirecting to login page
        if (ctx.Request.Path.StartsWithSegments("/api") ||
            ctx.Request.Path.StartsWithSegments("/todos") ||
            ctx.Request.Path.StartsWithSegments("/categories"))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// OAuth providers (Google + GitHub)
builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId     = builder.Configuration["Auth:Google:ClientId"] ?? "placeholder";
        options.ClientSecret = builder.Configuration["Auth:Google:ClientSecret"] ?? "placeholder";
    })
    .AddGitHub(options =>
    {
        options.ClientId     = builder.Configuration["Auth:GitHub:ClientId"] ?? "placeholder";
        options.ClientSecret = builder.Configuration["Auth:GitHub:ClientSecret"] ?? "placeholder";
    })
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(ApiKeyAuthHandler.SchemeName, _ => { });

// Authorization — require auth by default; health + oauth callback are exempt
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(
            IdentityConstants.ApplicationScheme,
            ApiKeyAuthHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy("ApiKey", policy => policy
        .AddAuthenticationSchemes(ApiKeyAuthHandler.SchemeName)
        .RequireAuthenticatedUser());

var app = builder.Build();

// Auto-migrate both contexts in development / testing
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    await db.Database.MigrateAsync();
    var identityDb = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
    await identityDb.Database.MigrateAsync();
}

app.MapDefaultEndpoints();  // /health/live and /health/ready — no auth required
app.MapTodoEndpoints();
app.MapOperationEndpoints();
app.MapCategoryEndpoints();
app.MapAuthEndpoints();
app.MapHub<EventHub>("/hubs/events");

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.Run();

// Allow WebApplicationFactory to find Program class
public partial class Program { }
```

- [ ] **Step 2: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Api/TodoList.Api.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Api/Program.cs
git commit -m "feat(auth): wire Identity + OAuth + ApiKey auth into Program.cs"
```

---

### Task 6: Create Identity EF migration

- [ ] **Step 1: Add initial Identity migration**

```bash
cd /Users/jim/code/todo-patterns
dotnet ef migrations add InitialIdentity \
  --context AppIdentityDbContext \
  --project TodoList.Api/TodoList.Api.csproj \
  --output-dir Migrations/Identity
```

Expected: "Build succeeded." and new migration files appear in `TodoList.Api/Migrations/Identity/`.

- [ ] **Step 2: Commit the migration**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Api/Migrations/Identity/
git commit -m "feat(auth): add EF migration for ASP.NET Core Identity schema"
```

---

### Task 7: Update integration test fixture to bypass auth

The integration tests use `anonymous` as the userId (fallback from the claim lookup). After auth is added, endpoints that call `RequireAuthorization()` will return 401. We fix this by having the test factory inject a synthetic claim into every request.

- [ ] **Step 1: Update `TodoList.IntegrationTests/Fixtures/ApiFixture.cs`**

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using TodoList.Api.Auth;
using TodoList.Api.Data;

namespace TodoList.IntegrationTests.Fixtures;

// ---------------------------------------------------------------------------
// Minimal test auth handler — bypasses real auth and injects a fixed userId.
// ---------------------------------------------------------------------------
public class TestAuthOptions : AuthenticationSchemeOptions { }

public class TestAuthHandler(
    IOptionsMonitor<TestAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<TestAuthOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-001"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Email, "test@example.com")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// ---------------------------------------------------------------------------
// Api fixture
// ---------------------------------------------------------------------------
public class ApiFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql;

    public ApiFixture()
    {
        if (IsRunningInContainer())
        {
            Environment.SetEnvironmentVariable("TESTCONTAINERS_HOST_OVERRIDE", "host.docker.internal");
            Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        }

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
    }

    private static bool IsRunningInContainer() =>
        File.Exists("/.dockerenv") ||
        (Environment.GetEnvironmentVariable("REMOTE_CONTAINERS") is not null) ||
        (Environment.GetEnvironmentVariable("CODESPACES") is not null);

    private WebApplicationFactory<Program>? _factory;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:todolist", _sql.GetConnectionString());

                builder.ConfigureServices(services =>
                {
                    // Replace all real authentication with a single test scheme that
                    // injects a fixed user claim. This keeps all existing tests green
                    // without requiring real OAuth credentials.
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<TestAuthOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });

                    // Override authorization so that RequireAuthorization() accepts
                    // our test scheme.
                    services.AddAuthorizationBuilder()
                        .SetDefaultPolicy(
                            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                                TestAuthHandler.SchemeName)
                            .RequireAuthenticatedUser()
                            .Build());
                });
            });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        await db.Database.MigrateAsync();

        // Migrate identity schema too
        var identityDb = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        await identityDb.Database.MigrateAsync();

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _sql.DisposeAsync();
    }
}
```

- [ ] **Step 2: Build integration tests**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.IntegrationTests/TodoList.IntegrationTests.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.IntegrationTests/Fixtures/ApiFixture.cs
git commit -m "test(auth): inject test auth handler in ApiFixture to bypass OAuth"
```

---

### Task 8: Add /api/me integration test

- [ ] **Step 1: Create `TodoList.IntegrationTests/Auth/MeEndpointTests.cs`**

```csharp
using System.Net;

namespace TodoList.IntegrationTests.Auth;

[Trait("Category", "Integration")]
public class MeEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetMe_returns_200_with_userId()
    {
        var response = await fixture.Client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("userId").GetString().Should().Be("test-user-001");
        body.GetProperty("email").GetString().Should().Be("test@example.com");
    }
}
```

- [ ] **Step 2: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.IntegrationTests/TodoList.IntegrationTests.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.IntegrationTests/Auth/MeEndpointTests.cs
git commit -m "test(auth): add /api/me integration test"
```

---

### Task 9: Run all integration tests

- [ ] **Step 1: Run the full integration test suite**

```bash
cd /Users/jim/code/todo-patterns
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj \
  --logger "console;verbosity=normal"
```

Expected: All tests pass. If any fail, fix before proceeding.

- [ ] **Step 2: Commit final status**

If tests pass without changes, no additional commit needed. If fixes were required, commit them with a message describing what was fixed.
