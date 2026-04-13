// TodoList.Mcp.Tools/ApiClient/TodoApiClient.cs
using System.Net.Http.Json;
using System.Text.Json;

namespace TodoList.Mcp.Tools.ApiClient;

/// <summary>
/// Typed HTTP client that wraps the TodoList.Api REST endpoints.
/// Registered as a typed client in DI — HttpClient is injected by the framework.
/// </summary>
public class TodoApiClient(HttpClient http)
{
    // -----------------------------------------------------------------------
    // Todos
    // -----------------------------------------------------------------------

    public async Task<JsonElement[]> ListTodosAsync(CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<JsonElement[]>("/todos", ct);
        return result ?? [];
    }

    public async Task<JsonElement?> GetTodoAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<JsonElement>($"/todos/{id}", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CreateTodoResult> CreateTodoAsync(string title, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/todos", new { title }, ct);
        response.EnsureSuccessStatusCode();

        var retryAfterMs = response.Headers.TryGetValues("X-Retry-After-Ms", out var vals)
            ? int.Parse(vals.First())
            : 200;

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var operationId = body.GetProperty("operationId").GetGuid();
        return new CreateTodoResult(operationId, retryAfterMs);
    }

    public async Task<OperationResult> CompleteTodoAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/todos/{id}/complete", null, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new OperationResult(null, "not_found", "Todo not found");

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            return new OperationResult(null, "error", err);
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var operationId = body.GetProperty("operationId").GetGuid();
        return new OperationResult(operationId, "accepted", null);
    }

    public async Task<OperationResult> DeleteTodoAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/todos/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new OperationResult(null, "not_found", "Todo not found");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var operationId = body.GetProperty("operationId").GetGuid();
        return new OperationResult(operationId, "accepted", null);
    }

    // -----------------------------------------------------------------------
    // Operations
    // -----------------------------------------------------------------------

    public async Task<JsonElement?> GetOperationAsync(Guid operationId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<JsonElement>($"/todos/operations/{operationId}", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}

public record CreateTodoResult(Guid OperationId, int RetryAfterMs);
public record OperationResult(Guid? OperationId, string Status, string? Error);
