// TodoList.IntegrationTests/Mcp/CompositeExecuteTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TodoList.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public class CompositeExecuteTests
{
    [Fact]
    public async Task Plan_endpoint_returns_capability_list()
    {
        // /plan requires no API calls — safe to point at a non-existent URL
        using var factory = new WebApplicationFactory<CompositeProgram>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ApiBaseUrl", "http://localhost:9999");
            });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/plan",
            new { about = "how do I create a todo?" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("capabilities").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Execute_with_unknown_operation_returns_failed_entry()
    {
        using var factory = new WebApplicationFactory<CompositeProgram>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ApiBaseUrl", "http://localhost:9999");
            });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/execute", new
        {
            operations = new[]
            {
                new { op = "unknown_operation", @params = new { } }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("failed").GetArrayLength().Should().Be(1);
        body.GetProperty("results").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Execute_skips_dependent_op_when_upstream_fails()
    {
        using var factory = new WebApplicationFactory<CompositeProgram>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ApiBaseUrl", "http://localhost:9999");
            });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/execute", new
        {
            operations = new object[]
            {
                // Op 0 will fail (unknown op)
                new { op = "unknown_op", @params = new { } },
                // Op 1 depends on $result[0] — should be skipped
                new { op = "complete_todo", @params = new { id = "$result[0].id" } }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Both should be in failed — op 0 as failed, op 1 as skipped
        body.GetProperty("failed").GetArrayLength().Should().Be(2);
        var failedItems = body.GetProperty("failed").EnumerateArray().ToArray();
        failedItems.Should().Contain(f => f.GetProperty("status").GetString() == "skipped");
    }
}
