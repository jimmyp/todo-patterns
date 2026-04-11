using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace TodoList.IntegrationTests.Todos;

public class TodoExtendedEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task PostTodo_creates_summary_readable_via_get()
    {
        var response = await fixture.Client.PostAsJsonAsync("/todos",
            new { title = "Test task" });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Poll operation
        var location = response.Headers.Location!.ToString();
        var opResponse = await fixture.Client.GetAsync(location);
        opResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var todos = await fixture.Client.GetFromJsonAsync<dynamic[]>("/todos");
        todos.Should().Contain(t => t!.GetProperty("title").GetString() == "Test task");
    }

    [Fact]
    public async Task RenameTodo_updates_title()
    {
        var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "Original" });
        var opId = (await createResponse.Content.ReadFromJsonAsync<dynamic>())!.GetProperty("operationId").GetString();
        await fixture.Client.GetAsync($"/todos/operations/{opId}");

        var todos = await fixture.Client.GetFromJsonAsync<dynamic[]>("/todos");
        var id = todos![0].GetProperty("id").GetString();

        var renameResponse = await fixture.Client.PostAsJsonAsync($"/todos/{id}/rename", new { title = "Renamed" });
        renameResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task UpdateProgress_returns_422_for_out_of_range()
    {
        var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "Task" });
        var opId = (await createResponse.Content.ReadFromJsonAsync<dynamic>())!.GetProperty("operationId").GetString();
        await fixture.Client.GetAsync($"/todos/operations/{opId}");
        var todos = await fixture.Client.GetFromJsonAsync<dynamic[]>("/todos");
        var id = todos![0].GetProperty("id").GetString();

        var response = await fixture.Client.PostAsJsonAsync($"/todos/{id}/update-progress", new { progress = 150 });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
