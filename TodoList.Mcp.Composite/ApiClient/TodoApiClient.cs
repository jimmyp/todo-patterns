// TodoList.Mcp.Composite/ApiClient/TodoApiClient.cs
using System.Net.Http.Json;
using System.Text.Json;

namespace TodoList.Mcp.Composite.ApiClient;

/// <summary>
/// Typed HTTP client for calling the TodoList.Api from Mcp.Composite.
/// Supports all operations needed by the composite executor.
/// </summary>
public class TodoApiClient(HttpClient http)
{
    public async Task<JsonElement[]> ListTodosAsync(CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<JsonElement[]>("/todos", ct);
        return result ?? [];
    }

    public async Task<JsonElement?> GetTodoAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/todos/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public async Task<JsonElement> CreateTodoAsync(string title, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/todos", new { title }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public async Task<JsonElement> CompleteTodoAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/todos/{id}/complete", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public async Task<JsonElement> DeleteTodoAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/todos/{id}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public async Task<JsonElement?> GetOperationAsync(Guid operationId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/todos/operations/{operationId}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
}
