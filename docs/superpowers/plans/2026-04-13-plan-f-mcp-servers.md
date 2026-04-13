# Plan F: MCP Servers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `TodoList.Mcp.Tools` (standard MCP server exposing todo CRUD tools via the official ModelContextProtocol SDK) and `TodoList.Mcp.Composite` (plan/execute composite endpoint that allows multi-step operations in a single round trip).

**Architecture:** `Mcp.Tools` is an ASP.NET Core server that registers tools via the official `ModelContextProtocol.Server` SDK. Each tool maps 1:1 to an API HTTP call — the MCP server calls the API as an HTTP client. `Mcp.Composite` is a plain ASP.NET Core minimal API with two endpoints (`POST /plan` and `POST /execute`). `/plan` returns capability descriptions for a given intent. `/execute` runs a sequence of operations with `$result[N].field` reference resolution, returning all results in a single response. Both projects call the API via `HttpClient` with an API key header. The AppHost wires both into the Aspire service graph.

**Tech Stack:** .NET 10, ModelContextProtocol.Server (official Anthropic .NET SDK), ASP.NET Core Minimal API, System.Text.Json

> **Read before starting:** `TodoList.Mcp.Tools/Program.cs`, `TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj`, `TodoList.Mcp.Composite/Program.cs`, `TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj`, `TodoList.AppHost/Program.cs`, `TodoList.AppHost/TodoList.AppHost.csproj`, `docs/superpowers/specs/2026-03-21-sample-architecture-design.md` (sections 6 and 7)

---

## File Map

### New/Modified: `TodoList.Mcp.Tools/`
```
TodoList.Mcp.Tools/Program.cs                      # register MCP server + tools
TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj       # add ModelContextProtocol.Server package
TodoList.Mcp.Tools/Tools/TodoTools.cs              # MCP tool implementations
TodoList.Mcp.Tools/Tools/OperationTools.cs         # get_operation tool
TodoList.Mcp.Tools/ApiClient/TodoApiClient.cs      # typed HttpClient wrapping the Api
```

### New/Modified: `TodoList.Mcp.Composite/`
```
TodoList.Mcp.Composite/Program.cs                  # register composite endpoints
TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj # add packages
TodoList.Mcp.Composite/Endpoints/PlanEndpoint.cs   # POST /plan — capability discovery
TodoList.Mcp.Composite/Endpoints/ExecuteEndpoint.cs # POST /execute — multi-step execution
TodoList.Mcp.Composite/ApiClient/TodoApiClient.cs  # typed HttpClient wrapping the Api
TodoList.Mcp.Composite/Models/CompositeModels.cs   # request/response records
```

### Modified: `TodoList.AppHost/`
```
TodoList.AppHost/Program.cs                        # add Mcp.Tools and Mcp.Composite to Aspire graph
TodoList.AppHost/TodoList.AppHost.csproj           # add project references
```

### New: `TodoList.IntegrationTests/`
```
TodoList.IntegrationTests/Mcp/CompositeExecuteTests.cs  # tests for /execute endpoint
```

---

## Tasks

### Task 1: Add ModelContextProtocol.Server package to Mcp.Tools

- [ ] **Step 1: Add package**

```bash
cd /Users/jim/code/todo-patterns
dotnet add TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj package ModelContextProtocol.AspNetCore --version 0.2.0-preview.3
```

If the exact version is not available, use the latest preview:

```bash
dotnet add TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj package ModelContextProtocol.AspNetCore --prerelease
```

Expected: package added without errors.

- [ ] **Step 2: Build to confirm**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj
git commit -m "feat(mcp): add ModelContextProtocol.AspNetCore package to Mcp.Tools"
```

---

### Task 2: Create TodoApiClient for Mcp.Tools

- [ ] **Step 1: Create `TodoList.Mcp.Tools/ApiClient/TodoApiClient.cs`**

```csharp
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
```

- [ ] **Step 2: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Mcp.Tools/ApiClient/
git commit -m "feat(mcp): add TodoApiClient typed HTTP client for Mcp.Tools"
```

---

### Task 3: Create MCP tools

- [ ] **Step 1: Create `TodoList.Mcp.Tools/Tools/TodoTools.cs`**

```csharp
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
```

- [ ] **Step 2: Create `TodoList.Mcp.Tools/Tools/OperationTools.cs`**

```csharp
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
```

- [ ] **Step 3: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Mcp.Tools/Tools/
git commit -m "feat(mcp): add todo and operation MCP tools"
```

---

### Task 4: Wire MCP server in Mcp.Tools Program.cs

- [ ] **Step 1: Replace `TodoList.Mcp.Tools/Program.cs`**

```csharp
// TodoList.Mcp.Tools/Program.cs
using TodoList.Mcp.Tools.ApiClient;
using TodoList.Mcp.Tools.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Typed HTTP client pointing at the Api service.
// In Aspire, the URL is injected via service discovery.
// Outside Aspire, configure "ApiBaseUrl" in appsettings.
builder.Services.AddHttpClient<TodoApiClient>(client =>
{
    var baseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl);
    // API key for authentication (set in appsettings or environment)
    var apiKey = builder.Configuration["Auth:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
});

// Register MCP server with all tool types in this assembly
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");

app.Run();
```

- [ ] **Step 2: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Mcp.Tools/Program.cs
git commit -m "feat(mcp): wire MCP server in Mcp.Tools Program.cs"
```

---

### Task 5: Build Mcp.Composite models and API client

- [ ] **Step 1: Create `TodoList.Mcp.Composite/Models/CompositeModels.cs`**

```csharp
// TodoList.Mcp.Composite/Models/CompositeModels.cs
using System.Text.Json;

namespace TodoList.Mcp.Composite.Models;

// ---------------------------------------------------------------------------
// /plan request + response
// ---------------------------------------------------------------------------

public record PlanRequest(string About);

public record PlanResponse(
    CapabilityDescription[] Capabilities,
    Dictionary<string, object> Schemas,
    PlanExample[] Examples);

public record CapabilityDescription(
    string Name,
    string Description,
    Dictionary<string, string> Parameters,
    string Returns);

public record PlanExample(
    string Description,
    PlanOperation[] Operations);

public record PlanOperation(string Op, Dictionary<string, string> Params);

// ---------------------------------------------------------------------------
// /execute request + response
// ---------------------------------------------------------------------------

public record ExecuteRequest(ExecuteOperation[] Operations);

public record ExecuteOperation(string Op, Dictionary<string, JsonElement> Params);

public record ExecuteResponse(
    ExecuteResult[] Results,
    ExecuteFailed[] Failed);

public record ExecuteResult(int Index, string Status, JsonElement? Result);

public record ExecuteFailed(
    int Index,
    string Status,
    string Reason,
    string? Detail = null,
    int? DependencyIndex = null);
```

- [ ] **Step 2: Create `TodoList.Mcp.Composite/ApiClient/TodoApiClient.cs`**

```csharp
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
```

- [ ] **Step 3: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Mcp.Composite/Models/ TodoList.Mcp.Composite/ApiClient/
git commit -m "feat(composite): add models and TodoApiClient for Mcp.Composite"
```

---

### Task 6: Create /plan endpoint

- [ ] **Step 1: Create `TodoList.Mcp.Composite/Endpoints/PlanEndpoint.cs`**

```csharp
// TodoList.Mcp.Composite/Endpoints/PlanEndpoint.cs
using TodoList.Mcp.Composite.Models;

namespace TodoList.Mcp.Composite.Endpoints;

public static class PlanEndpoint
{
    // Static capability catalog — describes all operations the composite executor supports.
    private static readonly CapabilityDescription[] AllCapabilities =
    [
        new("list_todos",    "List all todos",         new(), "JsonElement[] — array of todo objects"),
        new("get_todo",      "Get a single todo",      new() { ["id"] = "string (GUID, required)" }, "JsonElement — todo object or null"),
        new("create_todo",   "Create a new todo",      new() { ["title"] = "string (required, max 500 chars)" }, "{ operationId, retryAfterMs }"),
        new("complete_todo", "Mark a todo as complete",new() { ["id"] = "string (GUID, required)" }, "{ operationId, retryAfterMs }"),
        new("delete_todo",   "Delete a todo",          new() { ["id"] = "string (GUID, required)" }, "{ operationId, retryAfterMs }"),
        new("get_operation", "Poll an async operation",new() { ["operationId"] = "string (GUID, required)" }, "{ status, result? }"),
    ];

    private static readonly Dictionary<string, object> Schemas = new()
    {
        ["todo"] = new { id = "string (GUID)", title = "string", isCompleted = "bool", createdAt = "datetime", completedAt = "datetime?" },
        ["operation"] = new { id = "string (GUID)", status = "pending|processing|complete|failed", result = "object?" }
    };

    private static readonly PlanExample[] Examples =
    [
        new("Create then complete a todo",
        [
            new("create_todo", new() { ["title"] = "buy milk" }),
            new("complete_todo", new() { ["id"] = "$result[0].operationId" })
        ]),
        new("List all todos and delete the first one",
        [
            new("list_todos", new()),
            new("delete_todo", new() { ["id"] = "$result[0][0].id" })
        ])
    ];

    public static void MapPlanEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/plan", (PlanRequest request) =>
        {
            // Simple keyword-based capability filtering. Returns all capabilities
            // if no useful keywords are found, or filters to relevant ones.
            var words = request.About.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var relevant = AllCapabilities.Where(c =>
                words.Any(w =>
                    c.Name.Contains(w) ||
                    c.Description.ToLowerInvariant().Contains(w) ||
                    w is "all" or "how" or "?" or "create" or "list" or "get" or "complete" or "delete"))
                .ToArray();

            if (relevant.Length == 0)
                relevant = AllCapabilities;

            return Results.Ok(new PlanResponse(relevant, Schemas, Examples));
        });
    }
}
```

- [ ] **Step 2: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Mcp.Composite/Endpoints/PlanEndpoint.cs
git commit -m "feat(composite): add POST /plan capability discovery endpoint"
```

---

### Task 7: Create /execute endpoint

- [ ] **Step 1: Create `TodoList.Mcp.Composite/Endpoints/ExecuteEndpoint.cs`**

```csharp
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
```

- [ ] **Step 2: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Mcp.Composite/Endpoints/ExecuteEndpoint.cs
git commit -m "feat(composite): add POST /execute multi-step operation endpoint with result reference resolution"
```

---

### Task 8: Wire Mcp.Composite Program.cs

- [ ] **Step 1: Replace `TodoList.Mcp.Composite/Program.cs`**

```csharp
// TodoList.Mcp.Composite/Program.cs
using TodoList.Mcp.Composite.ApiClient;
using TodoList.Mcp.Composite.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Typed HTTP client for the Api
builder.Services.AddHttpClient<TodoApiClient>(client =>
{
    var baseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl);
    var apiKey = builder.Configuration["Auth:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
});

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapPlanEndpoint();
app.MapExecuteEndpoint();

app.Run();
```

- [ ] **Step 2: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Mcp.Composite/Program.cs
git commit -m "feat(composite): wire /plan and /execute endpoints in Mcp.Composite Program.cs"
```

---

### Task 9: Update AppHost to include Mcp.Tools and Mcp.Composite

- [ ] **Step 1: Add project references to AppHost.csproj**

```bash
cd /Users/jim/code/todo-patterns
dotnet add TodoList.AppHost/TodoList.AppHost.csproj reference TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj
dotnet add TodoList.AppHost/TodoList.AppHost.csproj reference TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj
```

Expected: references added.

- [ ] **Step 2: Replace `TodoList.AppHost/Program.cs`**

```csharp
// TodoList.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .AddDatabase("todolist");

var api = builder.AddProject<Projects.TodoList_Api>("api")
    .WithReference(sql)
    .WaitFor(sql);

builder.AddProject<Projects.TodoList_Web_Server>("web")
    .WithReference(api)
    .WaitFor(api);

builder.AddProject<Projects.TodoList_Mcp_Tools>("mcp-tools")
    .WithReference(api)
    .WaitFor(api);

builder.AddProject<Projects.TodoList_Mcp_Composite>("mcp-composite")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
```

- [ ] **Step 3: Build AppHost**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.AppHost/TodoList.AppHost.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.AppHost/TodoList.AppHost.csproj TodoList.AppHost/Program.cs
git commit -m "feat(mcp): add Mcp.Tools and Mcp.Composite to Aspire AppHost"
```

---

### Task 10: Add /execute integration tests

- [ ] **Step 1: Add a project reference from IntegrationTests to Mcp.Composite**

We test the composite server with its own WebApplicationFactory. Add a reference:

```bash
cd /Users/jim/code/todo-patterns
dotnet add TodoList.IntegrationTests/TodoList.IntegrationTests.csproj reference TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj
```

- [ ] **Step 2: Create `TodoList.IntegrationTests/Mcp/CompositeExecuteTests.cs`**

These tests use an in-process instance of the Composite server with a WireMock stub for the Api calls. Since adding WireMock is a larger dependency, the initial tests use a minimal approach: boot the composite server and verify the endpoint returns the expected shape for a simple in-memory plan.

```csharp
// TodoList.IntegrationTests/Mcp/CompositeExecuteTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TodoList.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public class CompositeExecuteTests
{
    private static HttpClient CreateCompositeClient()
    {
        // Boot composite server without a real Api — unknown_op will fail gracefully
        var factory = new WebApplicationFactory<global::Program>();
        return factory.CreateClient();
    }

    [Fact]
    public async Task Plan_endpoint_returns_capability_list()
    {
        // Start the composite factory pointing at a dummy API base URL
        // (no real API needed for /plan)
        using var factory = new WebApplicationFactory<global::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ApiBaseUrl", "http://localhost:9999"); // won't be called for /plan
            });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/plan",
            new { about = "how do I create a todo?" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("capabilities").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Execute_with_unknown_operation_returns_failed_entry()
    {
        using var factory = new WebApplicationFactory<global::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ApiBaseUrl", "http://localhost:9999");
            });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/execute", new
        {
            operations = new[]
            {
                new { op = "unknown_operation", @params = new { } }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("failed").GetArrayLength().Should().Be(1);
        body.GetProperty("results").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Execute_skips_dependent_op_when_upstream_fails()
    {
        using var factory = new WebApplicationFactory<global::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ApiBaseUrl", "http://localhost:9999");
            });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/execute", new
        {
            operations = new object[]
            {
                // Op 0 will fail (unknown op)
                new { op = "unknown_op", @params = new { } },
                // Op 1 depends on $result[0] — should be skipped
                new { op = "complete_todo", @params = new { id = "$result[0].id" } }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Both should be in failed — op 0 as failed, op 1 as skipped
        body.GetProperty("failed").GetArrayLength().Should().Be(2);
        var failedItems = body.GetProperty("failed").EnumerateArray().ToArray();
        failedItems.Should().Contain(f => f.GetProperty("status").GetString() == "skipped");
    }
}
```

Note: `global::Program` refers to the Composite server's `Program` class, not the Api's. Since both projects reference `Program`, we need a disambiguating alias. The `WebApplicationFactory<global::Program>` in a project that references only `TodoList.Mcp.Composite` will find the composite `Program`.

However, since `TodoList.IntegrationTests` already references `TodoList.Api` (which has its own `public partial class Program`), there will be an ambiguity. To resolve this, add a `[assembly: UserAssemblyMetadata(...)]` or use a wrapper type in the composite project.

**Resolution:** Create an entry point marker in Mcp.Composite:

```csharp
// Add to end of TodoList.Mcp.Composite/Program.cs:
public partial class CompositeProgram { }
```

Then use `WebApplicationFactory<CompositeProgram>` in the test. Update `CompositeExecuteTests.cs` to use `CompositeProgram` instead of `global::Program`.

- [ ] **Step 3: Add `public partial class CompositeProgram { }` to Mcp.Composite/Program.cs**

Edit `TodoList.Mcp.Composite/Program.cs` to append at the end:

```csharp
// Marker class for WebApplicationFactory in integration tests
public partial class CompositeProgram { }
```

And update all three test factory usages in `CompositeExecuteTests.cs` to use `CompositeProgram`:

```csharp
using var factory = new WebApplicationFactory<CompositeProgram>()
```

- [ ] **Step 4: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.IntegrationTests/TodoList.IntegrationTests.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 5: Run integration tests**

```bash
cd /Users/jim/code/todo-patterns
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj \
  --logger "console;verbosity=normal"
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Mcp.Composite/Program.cs TodoList.IntegrationTests/
git commit -m "test(composite): add integration tests for /plan and /execute endpoints"
```

---

### Task 11: Build entire solution

- [ ] **Step 1: Build solution**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.sln
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 2: Run all tests**

```bash
cd /Users/jim/code/todo-patterns
dotnet test TodoList.Tests/TodoList.Tests.csproj --logger "console;verbosity=normal"
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj --logger "console;verbosity=normal"
```

Expected: All tests pass.
