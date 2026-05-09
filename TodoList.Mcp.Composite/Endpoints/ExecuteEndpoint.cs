// TodoList.Mcp.Composite/Endpoints/ExecuteEndpoint.cs
using System.Text.Json;
using TodoList.Mcp.Composite.ApiClient;
using TodoList.Mcp.Composite.Models;

namespace TodoList.Mcp.Composite.Endpoints;

public static class ExecuteEndpoint
{
    public static void MapExecuteEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/execute", async (ExecuteRequest request, TodoApiClient api, CancellationToken ct) =>
        {
            var results = new Dictionary<int, JsonElement>();
            var failed = new List<ExecuteFailed>();

            for (int i = 0; i < request.Operations.Length; i++)
            {
                var op = request.Operations[i];

                // Check if any parameter references a failed upstream result
                var blockedBy = FindFailedDependency(op.Params, failed);
                if (blockedBy.HasValue)
                {
                    failed.Add(new ExecuteFailed(i, "skipped", "dependency_failed",
                        DependencyIndex: blockedBy.Value));
                    continue;
                }

                // Resolve $result[N].field references in params
                var resolvedParams = ResolveParams(op.Params, results);

                try
                {
                    var result = await DispatchAsync(op.Op, resolvedParams, api, ct);
                    results[i] = result;
                }
                catch (Exception ex)
                {
                    failed.Add(new ExecuteFailed(i, "failed", "execution_error", ex.Message));
                }
            }

            var successResults = results
                .Select(kvp => new ExecuteResult(kvp.Key, "complete", kvp.Value))
                .OrderBy(r => r.Index)
                .ToArray();

            return Results.Ok(new ExecuteResponse(successResults, [.. failed]));
        });
    }

    private static int? FindFailedDependency(
        Dictionary<string, JsonElement> @params,
        List<ExecuteFailed> failed)
    {
        foreach (var param in @params.Values)
        {
            if (param.ValueKind != JsonValueKind.String) continue;
            var str = param.GetString() ?? "";
            if (!str.StartsWith("$result[")) continue;

            // Parse $result[N] or $result[N].field
            var bracket = str.IndexOf(']');
            if (bracket < 0) continue;
            if (!int.TryParse(str[8..bracket], out var idx)) continue;

            if (failed.Any(f => f.Index == idx))
                return idx;
        }
        return null;
    }

    private static Dictionary<string, JsonElement> ResolveParams(
        Dictionary<string, JsonElement> @params,
        Dictionary<int, JsonElement> results)
    {
        var resolved = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in @params)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var str = value.GetString() ?? "";
                if (str.StartsWith("$result["))
                {
                    var resolvedValue = ResolveReference(str, results);
                    resolved[key] = resolvedValue;
                    continue;
                }
            }
            resolved[key] = value;
        }
        return resolved;
    }

    private static JsonElement ResolveReference(string reference, Dictionary<int, JsonElement> results)
    {
        // Format: $result[N] or $result[N].field or $result[N].field.subfield
        var bracket = reference.IndexOf(']');
        if (bracket < 0) return JsonDocument.Parse("null").RootElement;
        if (!int.TryParse(reference[8..bracket], out var idx)) return JsonDocument.Parse("null").RootElement;

        if (!results.TryGetValue(idx, out var element))
            return JsonDocument.Parse("null").RootElement;

        var remaining = reference[(bracket + 1)..];
        if (string.IsNullOrEmpty(remaining) || remaining == ".")
            return element;

        // Navigate property path: .field.subfield
        var parts = remaining.TrimStart('.').Split('.');
        var current = element;
        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return JsonDocument.Parse("null").RootElement;
            if (!current.TryGetProperty(part, out current))
                return JsonDocument.Parse("null").RootElement;
        }
        return current;
    }

    private static async Task<JsonElement> DispatchAsync(
        string op,
        Dictionary<string, JsonElement> @params,
        TodoApiClient api,
        CancellationToken ct)
    {
        return op switch
        {
            "list_todos" => JsonDocument.Parse(
                JsonSerializer.Serialize(await api.ListTodosAsync(ct))).RootElement,

            "get_todo" => (await api.GetTodoAsync(GetGuid(@params, "id"), ct))
                ?? JsonDocument.Parse("null").RootElement,

            "create_todo" => await api.CreateTodoAsync(GetString(@params, "title"), ct),

            "complete_todo" => await api.CompleteTodoAsync(GetGuid(@params, "id"), ct),

            "delete_todo" => await api.DeleteTodoAsync(GetGuid(@params, "id"), ct),

            "get_operation" => (await api.GetOperationAsync(GetGuid(@params, "operationId"), ct))
                ?? JsonDocument.Parse("null").RootElement,

            _ => throw new InvalidOperationException($"Unknown operation: {op}")
        };
    }

    private static string GetString(Dictionary<string, JsonElement> p, string key)
    {
        if (!p.TryGetValue(key, out var v))
            throw new InvalidOperationException($"Missing required parameter: {key}");
        return v.GetString() ?? throw new InvalidOperationException($"Parameter {key} must be a string");
    }

    private static Guid GetGuid(Dictionary<string, JsonElement> p, string key)
    {
        var str = GetString(p, key);
        if (!Guid.TryParse(str, out var guid))
            throw new InvalidOperationException($"Parameter {key} must be a valid GUID, got: {str}");
        return guid;
    }
}
