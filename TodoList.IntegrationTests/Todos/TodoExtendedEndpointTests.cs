using System.Net;

namespace TodoList.IntegrationTests.Todos;

public class TodoExtendedEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task PostTodo_creates_summary_readable_via_get()
    {
        await fixture.CreateTodoAsync("Test task");

        var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
        todos.Should().Contain(t => t.GetProperty("title").GetString() == "Test task");
    }

    [Fact]
    public async Task RenameTodo_updates_title()
    {
        var (todoId, _) = await fixture.CreateTodoAsync("Original");

        var renameResponse = await fixture.Client.PostAsJsonAsync($"/todos/{todoId}/rename", new { title = "Renamed" });
        renameResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var renameAccepted = await renameResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        await fixture.PollOperationAsync(renameAccepted!.OperationId);

        var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
        todos!.Should().Contain(t => t.GetProperty("id").GetGuid() == todoId
                                   && t.GetProperty("title").GetString() == "Renamed");
    }

    [Fact]
    public async Task UpdateProgress_with_invalid_value_fails_operation()
    {
        var (todoId, _) = await fixture.CreateTodoAsync("Task");

        // Fire-and-forget: endpoint returns 202, but operation will fail
        var response = await fixture.Client.PostAsJsonAsync($"/todos/{todoId}/update-progress", new { progress = 150 });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var op = await fixture.PollOperationAsync(accepted!.OperationId);

        op.GetProperty("status").GetString().Should().Be("failed");
    }
}
