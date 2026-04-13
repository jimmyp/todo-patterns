// TodoList.Mcp.Tools/Tools/TodoTools.cs
using System.ComponentModel;
using ModelContextProtocol.Server;
using TodoList.Mcp.Tools.ApiClient;

namespace TodoList.Mcp.Tools.Tools;

/// <summary>
/// MCP tools for todo CRUD operations.
/// Each method maps to one API call. The [McpServerTool] attribute registers it
/// with the MCP server. JSON schema is auto-generated from parameter types.
/// </summary>
[McpServerToolType]
public class TodoTools(TodoApiClient api)
{
    [McpServerTool, Description("List all todos for the current user")]
    public async Task<string> list_todos(CancellationToken ct = default)
    {
        var todos = await api.ListTodosAsync(ct);
        return System.Text.Json.JsonSerializer.Serialize(todos);
    }

    [McpServerTool, Description("Get a single todo by ID")]
    public async Task<string> get_todo(
        [Description("The todo ID (GUID)")] string id,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return """{"error": "Invalid GUID format"}""";

        var todo = await api.GetTodoAsync(guid, ct);
        if (todo is null)
            return """{"error": "Not found"}""";

        return System.Text.Json.JsonSerializer.Serialize(todo);
    }

    [McpServerTool, Description("Create a new todo item")]
    public async Task<string> create_todo(
        [Description("Todo title (required, max 500 characters)")] string title,
        CancellationToken ct = default)
    {
        var result = await api.CreateTodoAsync(title, ct);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            operationId = result.OperationId,
            retryAfterMs = result.RetryAfterMs,
            message = "Todo creation accepted. Poll get_operation to confirm."
        });
    }

    [McpServerTool, Description("Mark a todo as complete")]
    public async Task<string> complete_todo(
        [Description("The todo ID (GUID)")] string id,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return """{"error": "Invalid GUID format"}""";

        var result = await api.CompleteTodoAsync(guid, ct);
        if (result.Error is not null)
            return System.Text.Json.JsonSerializer.Serialize(new { error = result.Error });

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            operationId = result.OperationId,
            status = result.Status
        });
    }

    [McpServerTool, Description("Delete a todo item")]
    public async Task<string> delete_todo(
        [Description("The todo ID (GUID)")] string id,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return """{"error": "Invalid GUID format"}""";

        var result = await api.DeleteTodoAsync(guid, ct);
        if (result.Error is not null)
            return System.Text.Json.JsonSerializer.Serialize(new { error = result.Error });

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            operationId = result.OperationId,
            status = result.Status
        });
    }
}
