using System.Net;

namespace TodoList.IntegrationTests.Api;

[Trait("Category", "Integration")]
[Collection(ApiCollection.Name)]
public class ConcurrencyTests(ApiFixture fixture)
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

        // Second operation should fail with version conflict — assert on the typed
        // FailureCode (the contract). The human-readable FailureReason is
        // intentionally not the source of truth (spec §1).
        op2.GetProperty("status").GetString().Should().Be("failed");
        op2.GetProperty("failureCode").GetString().Should().Be("VERSION_CONFLICT");
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

    [Fact]
    public async Task Sequential_category_mutations_carry_version_forward()
    {
        // Trigger auto-seed so the CategoryList aggregate exists with Version = 0.
        var seeded = await fixture.Client.GetFromJsonAsync<JsonElement>("/categories");
        var startVersion = seeded.GetProperty("version").GetInt32();

        // First mutation succeeds with the current version (auto-seeds may bump it).
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/categories");
        req1.Content = JsonContent.Create(new { name = "Side", color = "#123456", icon = "star" });
        req1.Headers.Add("X-Expected-Version", startVersion.ToString());
        var accepted1 = await (await fixture.Client.SendAsync(req1)).Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var op1 = await fixture.PollOperationAsync(accepted1!.OperationId);
        op1.GetProperty("status").GetString().Should().Be("complete");

        // After the first mutation lands, GET should report a higher Version. If B3
        // were broken (no persistence) this would still be `startVersion` and the
        // second mutation below would either silently skip the check or fail with
        // VERSION_CONFLICT depending on which side of zero we ended up on.
        var afterFirst = await fixture.Client.GetFromJsonAsync<JsonElement>("/categories");
        var bumpedVersion = afterFirst.GetProperty("version").GetInt32();
        bumpedVersion.Should().BeGreaterThan(startVersion);

        // Second mutation succeeds when we send the *new* version.
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/categories");
        req2.Content = JsonContent.Create(new { name = "Hobby", color = "#abcdef", icon = "star" });
        req2.Headers.Add("X-Expected-Version", bumpedVersion.ToString());
        var accepted2 = await (await fixture.Client.SendAsync(req2)).Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var op2 = await fixture.PollOperationAsync(accepted2!.OperationId);
        op2.GetProperty("status").GetString().Should().Be("complete");

        // Third mutation with the now-stale `bumpedVersion` should conflict.
        var req3 = new HttpRequestMessage(HttpMethod.Post, "/categories");
        req3.Content = JsonContent.Create(new { name = "Stale", color = "#ff00ff", icon = "star" });
        req3.Headers.Add("X-Expected-Version", bumpedVersion.ToString());
        var accepted3 = await (await fixture.Client.SendAsync(req3)).Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var op3 = await fixture.PollOperationAsync(accepted3!.OperationId);
        op3.GetProperty("status").GetString().Should().Be("failed");
        op3.GetProperty("failureCode").GetString().Should().Be("VERSION_CONFLICT");
    }
}
