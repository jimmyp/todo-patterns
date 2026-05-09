// TodoList.Mcp.Composite/Endpoints/PlanEndpoint.cs
using TodoList.Mcp.Composite.Models;

namespace TodoList.Mcp.Composite.Endpoints;

public static class PlanEndpoint
{
    // Static capability catalog — describes all operations the composite executor supports.
    private static readonly CapabilityDescription[] AllCapabilities =
    [
        new("list_todos",    "List all todos",          new(), "JsonElement[] — array of todo objects"),
        new("get_todo",      "Get a single todo",       new() { ["id"] = "string (GUID, required)" }, "JsonElement — todo object or null"),
        new("create_todo",   "Create a new todo",       new() { ["title"] = "string (required, max 500 chars)" }, "{ operationId, retryAfterMs }"),
        new("complete_todo", "Mark a todo as complete", new() { ["id"] = "string (GUID, required)" }, "{ operationId, retryAfterMs }"),
        new("delete_todo",   "Delete a todo",           new() { ["id"] = "string (GUID, required)" }, "{ operationId, retryAfterMs }"),
        new("get_operation", "Poll an async operation", new() { ["operationId"] = "string (GUID, required)" }, "{ status, result? }"),
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
