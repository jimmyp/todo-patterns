using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using TodoList.Api.Auth;
using TodoList.Api.Data;

namespace TodoList.IntegrationTests.Fixtures;

/// <summary>
/// Replaces all real auth schemes with a test scheme that injects a fixed test-user-001 claim.
/// This lets all endpoints work without real OAuth / cookie flow in integration tests.
/// </summary>
public class TestAuthOptions : AuthenticationSchemeOptions { }

public class TestAuthHandler(
    IOptionsMonitor<TestAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<TestAuthOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string TestUserId = "test-user-001";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestUserId),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim("auth_method", "test")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class ApiFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql;

    public ApiFixture()
    {
        // Inside a dev container using docker-outside-of-docker, mapped ports are accessible on
        // the host machine, not on localhost inside the container — use host.docker.internal.
        // Outside a dev container (native macOS/Linux), localhost works fine.
        if (IsRunningInContainer())
        {
            Environment.SetEnvironmentVariable("TESTCONTAINERS_HOST_OVERRIDE", "host.docker.internal");
            // Ryuk can't bind-mount the Docker socket in docker-outside-of-docker.
            Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        }

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
    }

    private static bool IsRunningInContainer() =>
        File.Exists("/.dockerenv") ||
        (Environment.GetEnvironmentVariable("REMOTE_CONTAINERS") is not null) ||
        (Environment.GetEnvironmentVariable("CODESPACES") is not null);

    private WebApplicationFactory<ApiProgram>? _factory;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        _factory = new WebApplicationFactory<ApiProgram>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                // Override the Aspire connection string so AddSqlServerDbContext("todolist")
                // resolves to the test container instead of the Aspire service catalog.
                builder.UseSetting("ConnectionStrings:todolist", _sql.GetConnectionString());

                builder.ConfigureServices(services =>
                {
                    // Remove all real authentication and replace with test scheme
                    var authDescriptors = services
                        .Where(d => d.ServiceType == typeof(IAuthenticationSchemeProvider)
                                    || d.ServiceType == typeof(IAuthenticationHandlerProvider)
                                    || d.ServiceType == typeof(IAuthenticationService))
                        .ToList();

                    // Register the test auth handler on top of existing services
                    services
                        .AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

                    // Replace default authorization policy to accept only test scheme
                    services.AddAuthorization(options =>
                    {
                        options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                            .RequireAuthenticatedUser()
                            .Build();
                        options.FallbackPolicy = null;
                    });
                });
            });

        // Apply EF migrations against the test container DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        await db.Database.MigrateAsync();

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
