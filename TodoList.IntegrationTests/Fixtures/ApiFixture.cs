using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using TodoList.Api.Data;

namespace TodoList.IntegrationTests.Fixtures;

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

    private WebApplicationFactory<Program>? _factory;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                // Override the Aspire connection string so AddSqlServerDbContext("todolist")
                // resolves to the test container instead of the Aspire service catalog.
                builder.UseSetting("ConnectionStrings:todolist", _sql.GetConnectionString());
            });

        // Apply EF migrations against the test container DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        await db.Database.MigrateAsync();

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _sql.DisposeAsync();
    }
}
