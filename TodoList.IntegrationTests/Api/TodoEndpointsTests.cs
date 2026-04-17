using System.Net;

namespace TodoList.IntegrationTests.Api;

[Trait("Category", "Integration")]
public class TodoEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetTodos_returns_json_array()
    {
        var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
        todos.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTodo_not_found_returns_404()
    {
        var response = await fixture.Client.GetAsync($"/todos/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostTodo_returns_202_with_location_and_retry_header()
    {
        var response = await fixture.Client.PostAsJsonAsync("/todos", new { title = "buy milk" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Should().ContainKey("X-Retry-After-Ms");
    }

    [Fact]
    public async Task PostTodo_todo_appears_in_get_list()
    {
        await fixture.CreateTodoAsync("integration test todo");

        var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
        todos!.Should().Contain(t => t.GetProperty("title").GetString() == "integration test todo");
    }

    [Fact]
    public async Task CompleteTodo_returns_202()
    {
        var (todoId, _) = await fixture.CreateTodoAsync("complete me");

        var completeResponse = await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task UncompleteTodo_after_complete_returns_202()
    {
        var (todoId, _) = await fixture.CreateTodoAsync("uncomplete me");

        // Complete
        var completeResp = await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);
        var completeAccepted = await completeResp.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        await fixture.PollOperationAsync(completeAccepted!.OperationId);

        // Uncomplete
        var uncompleteResponse = await fixture.Client.PostAsync($"/todos/{todoId}/uncomplete", null);
        uncompleteResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task DeleteTodo_returns_202_and_todo_disappears_from_list()
    {
        var (todoId, _) = await fixture.CreateTodoAsync("delete me");

        var deleteResponse = await fixture.Client.DeleteAsync($"/todos/{todoId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var deleteAccepted = await deleteResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        await fixture.PollOperationAsync(deleteAccepted!.OperationId);

        var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
        todos!.Should().NotContain(t => t.GetProperty("id").GetGuid() == todoId);
    }
}
