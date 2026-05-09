using System.Net;
using TodoList.Domain.ReadModels;

namespace TodoList.IntegrationTests.Auth;

/// <summary>
/// Verifies that a user only sees their own data. We impersonate different users via the
/// X-Test-User header (see <see cref="TestAuthHandler"/>) and assert that user A's todos
/// and categories don't leak into user B's GET responses.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ApiCollection.Name)]
public class CrossUserIsolationTests(ApiFixture fixture)
{
    private const string UserA = "user-a-iso";
    private const string UserB = "user-b-iso";

    [Fact]
    public async Task User_cannot_see_another_users_todos()
    {
        // User A creates a todo
        var title = $"A-only-{Guid.NewGuid()}";
        var postA = new HttpRequestMessage(HttpMethod.Post, "/todos")
        {
            Content = JsonContent.Create(new { title })
        };
        postA.Headers.Add(TestAuthHandler.UserHeader, UserA);
        var postResponse = await fixture.Client.SendAsync(postA);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await postResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        await fixture.PollOperationAsync(accepted!.OperationId, userId: UserA);

        // User A sees their todo (projection is async — poll until it lands)
        JsonElement[] todosA = [];
        for (var i = 0; i < 20; i++)
        {
            var getA = new HttpRequestMessage(HttpMethod.Get, "/todos");
            getA.Headers.Add(TestAuthHandler.UserHeader, UserA);
            var responseA = await fixture.Client.SendAsync(getA);
            todosA = await responseA.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
            if (todosA.Any(t => t.GetProperty("title").GetString() == title)) break;
            await Task.Delay(100);
        }
        todosA.Should().Contain(t => t.GetProperty("title").GetString() == title);

        // User B must NOT see user A's todo
        var getB = new HttpRequestMessage(HttpMethod.Get, "/todos");
        getB.Headers.Add(TestAuthHandler.UserHeader, UserB);
        var responseB = await fixture.Client.SendAsync(getB);
        var todosB = await responseB.Content.ReadFromJsonAsync<JsonElement[]>();
        todosB!.Should().NotContain(t => t.GetProperty("title").GetString() == title);
    }

    [Fact]
    public async Task Users_have_independent_category_lists()
    {
        // User A triggers auto-seed + adds a custom category
        var getA = new HttpRequestMessage(HttpMethod.Get, "/categories");
        getA.Headers.Add(TestAuthHandler.UserHeader, UserA);
        (await fixture.Client.SendAsync(getA)).EnsureSuccessStatusCode();

        var customName = $"A-cat-{Guid.NewGuid()}";
        var postA = new HttpRequestMessage(HttpMethod.Post, "/categories")
        {
            Content = JsonContent.Create(new { name = customName, color = "#FF00FF", icon = "star" })
        };
        postA.Headers.Add(TestAuthHandler.UserHeader, UserA);
        var postResponse = await fixture.Client.SendAsync(postA);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await postResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        await fixture.PollOperationAsync(accepted!.OperationId, userId: UserA);

        // User B triggers their own auto-seed (fresh CategoryList)
        var getB = new HttpRequestMessage(HttpMethod.Get, "/categories");
        getB.Headers.Add(TestAuthHandler.UserHeader, UserB);
        var responseB = await fixture.Client.SendAsync(getB);
        var bodyB = await responseB.Content.ReadFromJsonAsync<JsonElement>();
        var namesB = bodyB.GetProperty("categories").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()!)
            .ToList();

        // User B must NOT see user A's custom category
        namesB.Should().NotContain(customName);
        // But they should see the default seeds
        namesB.Should().Contain("Personal");
    }

    [Fact]
    public async Task User_cannot_poll_another_users_operation()
    {
        // User A creates a todo — captures their operation id
        var postA = new HttpRequestMessage(HttpMethod.Post, "/todos")
        {
            Content = JsonContent.Create(new { title = $"isolated-op-{Guid.NewGuid()}" })
        };
        postA.Headers.Add(TestAuthHandler.UserHeader, UserA);
        var postResponse = await fixture.Client.SendAsync(postA);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await postResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        // User A can poll their own operation
        var pollA = new HttpRequestMessage(HttpMethod.Get, $"/todos/operations/{accepted!.OperationId}");
        pollA.Headers.Add(TestAuthHandler.UserHeader, UserA);
        var responseA = await fixture.Client.SendAsync(pollA);
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);

        // User B must NOT be able to poll user A's operation — 404, not 403, so the
        // response shape doesn't reveal whether the operation id exists.
        var pollB = new HttpRequestMessage(HttpMethod.Get, $"/todos/operations/{accepted.OperationId}");
        pollB.Headers.Add(TestAuthHandler.UserHeader, UserB);
        var responseB = await fixture.Client.SendAsync(pollB);
        responseB.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
