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
