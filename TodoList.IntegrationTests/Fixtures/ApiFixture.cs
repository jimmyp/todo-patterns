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
        // Docker-outside-of-docker: ports are mapped on the host (macOS), not on localhost inside
        // the dev container. Tell Testcontainers to connect via host.docker.internal instead.
        Environment.SetEnvironmentVariable("TESTCONTAINERS_HOST_OVERRIDE", "host.docker.internal");
        // Ryuk (cleanup container) can't bind-mount the Docker socket in docker-outside-of-docker.
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        _sql = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
    }

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
