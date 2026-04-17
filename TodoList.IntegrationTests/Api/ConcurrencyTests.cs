using System.Net;

namespace TodoList.IntegrationTests.Api;

[Trait("Category", "Integration")]
public class ConcurrencyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Concurrent_mutations_with_stale_version_fails_operation()
    {
        // Create a todo
        var (todoId, _) = await fixture.CreateTodoAsync("concurrency test");

        // First rename succeeds — sends ExpectedVersion = 1 (the version after Create)
        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/todos/{todoId}/rename");
        req1.Content = JsonContent.Create(new { title = "Renamed once" });
        req1.Headers.Add("X-Expected-Version", "1");
        var resp1 = await fixture.Client.SendAsync(req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted1 = await resp1.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var op1 = await fixture.PollOperationAsync(accepted1!.OperationId);
        op1.GetProperty("status").GetString().Should().Be("complete");

        // Second rename with same ExpectedVersion = 1 (stale — actual version is now 2)
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/todos/{todoId}/rename");
        req2.Content = JsonContent.Create(new { title = "Renamed twice" });
        req2.Headers.Add("X-Expected-Version", "1");
        var resp2 = await fixture.Client.SendAsync(req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted2 = await resp2.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var op2 = await fixture.PollOperationAsync(accepted2!.OperationId);

        // Second operation should fail with version conflict
        op2.GetProperty("status").GetString().Should().Be("failed");
        op2.GetProperty("failureReason").GetString().Should().Contain("Version conflict");
    }

    [Fact]
    public async Task Mutation_without_expected_version_always_succeeds()
    {
        // Create a todo
        var (todoId, _) = await fixture.CreateTodoAsync("no version check");

        // Rename without X-Expected-Version header (defaults to 0 = skip check)
        var renameResp = await fixture.Client.PostAsJsonAsync($"/todos/{todoId}/rename", new { title = "Updated" });
        renameResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await renameResp.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var op = await fixture.PollOperationAsync(accepted!.OperationId);
        op.GetProperty("status").GetString().Should().Be("complete");
    }
}
