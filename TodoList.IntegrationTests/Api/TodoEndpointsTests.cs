using System.Net;

namespace TodoList.IntegrationTests.Api;

[Trait("Category", "Integration")]
public class TodoEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetTodos_returns_json_array()
    {
        // Tests share a DB and run in undefined order — don't assert empty.
        // This test just verifies the endpoint returns a valid JSON array.
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
    public async Task PostTodo_with_empty_title_returns_400()
    {
        var response = await fixture.Client.PostAsJsonAsync("/todos", new { title = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTodo_todo_appears_in_get_list()
    {
        await fixture.Client.PostAsJsonAsync("/todos", new { title = "integration test todo" });

        var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
        todos!.Should().Contain(t => t.GetProperty("title").GetString() == "integration test todo");
    }

    [Fact]
    public async Task CompleteTodo_returns_202()
    {
        var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "complete me" });
        var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        var opResponse = await fixture.Client.GetFromJsonAsync<JsonElement>(
            $"/todos/operations/{created!.OperationId}");
        var todoId = opResponse.GetProperty("result").GetProperty("id").GetGuid();

        var completeResponse = await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task CompleteTodo_twice_returns_400()
    {
        var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "complete twice" });
        var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var opResponse = await fixture.Client.GetFromJsonAsync<JsonElement>(
            $"/todos/operations/{created!.OperationId}");
        var todoId = opResponse.GetProperty("result").GetProperty("id").GetGuid();

        await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);
        var secondComplete = await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);

        secondComplete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UncompleteTodo_after_complete_returns_202()
    {
        var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "uncomplete me" });
        var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var opResponse = await fixture.Client.GetFromJsonAsync<JsonElement>(
            $"/todos/operations/{created!.OperationId}");
        var todoId = opResponse.GetProperty("result").GetProperty("id").GetGuid();

        await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);
        var uncompleteResponse = await fixture.Client.PostAsync($"/todos/{todoId}/uncomplete", null);

        uncompleteResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task DeleteTodo_returns_202_and_todo_disappears_from_list()
    {
        var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "delete me" });
        var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var opResponse = await fixture.Client.GetFromJsonAsync<JsonElement>(
            $"/todos/operations/{created!.OperationId}");
        var todoId = opResponse.GetProperty("result").GetProperty("id").GetGuid();

        var deleteResponse = await fixture.Client.DeleteAsync($"/todos/{todoId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
        todos!.Should().NotContain(t => t.GetProperty("id").GetGuid() == todoId);
    }
}
