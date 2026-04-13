// TodoList.Mcp.Tools/Tools/OperationTools.cs
using System.ComponentModel;
using ModelContextProtocol.Server;
using TodoList.Mcp.Tools.ApiClient;

namespace TodoList.Mcp.Tools.Tools;

[McpServerToolType]
public class OperationTools(TodoApiClient api)
{
    [McpServerTool, Description("Poll an async operation by ID to check its status and result")]
    public async Task<string> get_operation(
        [Description("The operation ID (GUID) returned by create_todo, complete_todo, or delete_todo")]
        string operationId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(operationId, out var guid))
            return """{"error": "Invalid GUID format"}""";

        var op = await api.GetOperationAsync(guid, ct);
        if (op is null)
            return """{"error": "Operation not found"}""";

        return System.Text.Json.JsonSerializer.Serialize(op);
    }
}
