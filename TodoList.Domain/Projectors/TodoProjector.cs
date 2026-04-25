// TodoList.Domain/Projectors/TodoProjector.cs
using System.Text.Json;
using TodoList.Domain.ReadModels;

namespace TodoList.Domain.Projectors;

/// <summary>
/// Projects a stream of client-side event envelopes into TodoSummary read models.
/// Handles both domain events (e.g. TodoCreated) and seed snapshots (TodoSeeded).
/// </summary>
public static class TodoProjector
{
    public static List<TodoSummary> ProjectAll(IReadOnlyList<DomainEventEnvelope> events)
    {
        var state = new Dictionary<Guid, TodoSummary>();

        foreach (var evt in events.OrderBy(e => e.AggregateId).ThenBy(e => e.AggregateVersion))
        {
            if (!Guid.TryParse(evt.AggregateId, out var id)) continue;

            switch (evt.Type)
            {
                case "TodoSeeded":
                    if (evt.Payload is { } seedPayload)
                        state[id] = ProjectSeed(id, seedPayload) with { Version = evt.AggregateVersion };
                    break;

                case "TodoCreated":
                    if (evt.Payload is { } createdPayload)
                        state[id] = ProjectCreated(id, createdPayload) with { Version = evt.AggregateVersion };
                    break;

                case "TodoCompleted":
                    if (state.TryGetValue(id, out var toComplete))
                    {
                        var completedAt = evt.Payload?.TryGetProperty("completedAt", out var cat) == true
                            ? cat.GetDateTimeOffset()
                            : DateTimeOffset.UtcNow;
                        state[id] = toComplete with { IsCompleted = true, CompletedAt = completedAt, Progress = 100 };
                    }
                    break;

                case "TodoUncompleted":
                    if (state.TryGetValue(id, out var toUncomplete))
                        state[id] = toUncomplete with { IsCompleted = false, CompletedAt = null };
                    break;

                case "TodoDeleted":
                    state.Remove(id);
                    break;

                case "TodoRenamed":
                    if (state.TryGetValue(id, out var toRename) && evt.Payload is { } renamedPayload)
                    {
                        var newTitle = renamedPayload.TryGetProperty("newTitle", out var nt) ? nt.GetString() ?? toRename.Title : toRename.Title;
                        state[id] = toRename with { Title = newTitle };
                    }
                    break;

                case "TodoCategoryAssigned":
                    if (state.TryGetValue(id, out var toAssign) && evt.Payload is { } assignPayload)
                    {
                        var catIdStr = assignPayload.TryGetProperty("categoryId", out var cid) ? cid.GetString() : null;
                        if (Guid.TryParse(catIdStr, out var catId))
                            state[id] = toAssign with { CategoryId = catId };
                    }
                    break;

                case "TodoCategoryUnassigned":
                    if (state.TryGetValue(id, out var toUnassign))
                        state[id] = toUnassign with { CategoryId = null, CategoryName = null, CategoryColor = null };
                    break;

                case "TodoDueDateSet":
                    if (state.TryGetValue(id, out var toDueDate) && evt.Payload is { } dueDatePayload)
                    {
                        var dueDate = dueDatePayload.TryGetProperty("dueDate", out var dd) ? dd.GetDateTimeOffset() : (DateTimeOffset?)null;
                        state[id] = toDueDate with { DueDate = dueDate };
                    }
                    break;

                case "TodoDueDateCleared":
                    if (state.TryGetValue(id, out var toClearDate))
                        state[id] = toClearDate with { DueDate = null };
                    break;

                case "TodoNotesUpdated":
                    // Notes not in TodoSummary — no-op for projection
                    break;

                case "TodoProgressUpdated":
                    if (state.TryGetValue(id, out var toProgress) && evt.Payload is { } progressPayload)
                    {
                        var progress = progressPayload.TryGetProperty("progress", out var p) ? p.GetInt32() : toProgress.Progress;
                        state[id] = toProgress with { Progress = progress };
                    }
                    break;
            }

            // Stamp the latest aggregate version onto the projected summary so callers
            // (CommandDispatcher) carry the correct ExpectedVersion. The seed event's
            // AggregateVersion comes from the server's GET /todos response.
            if (state.TryGetValue(id, out var current) && evt.AggregateVersion > current.Version)
                state[id] = current with { Version = evt.AggregateVersion };
        }

        return state.Values.ToList();
    }

    private static TodoSummary ProjectSeed(Guid id, JsonElement payload)
    {
        return new TodoSummary
        {
            Id = id,
            Title = payload.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
            IsCompleted = payload.TryGetProperty("isCompleted", out var ic) && ic.GetBoolean(),
            CategoryId = payload.TryGetProperty("categoryId", out var cid) && cid.ValueKind != JsonValueKind.Null
                ? Guid.TryParse(cid.GetString(), out var catGuid) ? catGuid : (Guid?)null
                : null,
            CategoryName = payload.TryGetProperty("categoryName", out var cn) ? cn.GetString() : null,
            CategoryColor = payload.TryGetProperty("categoryColor", out var cc) ? cc.GetString() : null,
            DueDate = payload.TryGetProperty("dueDate", out var dd) && dd.ValueKind != JsonValueKind.Null
                ? dd.GetDateTimeOffset()
                : (DateTimeOffset?)null,
            Progress = payload.TryGetProperty("progress", out var prog) ? prog.GetInt32() : 0,
            CreatedAt = payload.TryGetProperty("createdAt", out var ca) ? ca.GetDateTimeOffset() : DateTimeOffset.UtcNow,
            CompletedAt = payload.TryGetProperty("completedAt", out var cat) && cat.ValueKind != JsonValueKind.Null
                ? cat.GetDateTimeOffset()
                : (DateTimeOffset?)null
        };
    }

    private static TodoSummary ProjectCreated(Guid id, JsonElement payload)
    {
        return new TodoSummary
        {
            Id = id,
            Title = payload.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
            IsCompleted = false,
            CategoryId = null,
            DueDate = null,
            Progress = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
