# Plan H: Review Fixes

**Date:** 2026-04-15
**Spec:** `docs/superpowers/specs/2026-04-15-review-fixes-design.md`
**Status:** Ready to execute

---

## Overview

Fixes all issues from the code review: optimistic concurrency, Wolverine dispatch, URL mismatches, saga detection, cascade deletes, client wiring, and minor cleanup. 14 tasks, executed sequentially, build-verified after each.

---

## Task 1: Add `Version` to read models and projection entities

Both `TodoSummary` and `CategorySummary` need a `Version` field so pages can pass real version numbers to `ExpectedVersion` instead of hardcoding `0`.

### Files to change

| File | Change |
|------|--------|
| `TodoList.Domain/ReadModels/TodoSummary.cs` | Add `public int Version { get; init; }` |
| `TodoList.Domain/ReadModels/CategorySummary.cs` | Add `public int Version { get; init; }` |
| `TodoList.Api/Data/Projections/TodoSummaryEntity.cs` | Add `public int Version { get; set; }` |
| `TodoList.Api/Data/Projections/CategorySummaryEntity.cs` | Add `public int Version { get; set; }` |

### Notes

- The `Todo` aggregate doesn't track `Version` today. The `CategoryList` aggregate does (`Version` property, incremented in mutating methods). For `Todo`, we need to add a `Version` property and increment it in each mutating method.
- `TodoList.Domain/Aggregates/Todo.cs` -- add `public int Version { get; private set; }` and increment it in every method that mutates state (Complete, Uncomplete, Delete, Rename, AssignCategory, UnassignCategory, SetDueDate, ClearDueDate, UpdateNotes, UpdateProgress). Set to 1 in `Create`.
- `TodoList.Api/EventHandlers/TodoProjectionHandler.cs` -- increment `Version` on each summary update.
- `TodoList.Api/EventHandlers/CategoryProjectionHandler.cs` -- increment `Version` on each summary update.
- No EF migration needed yet -- Version column gets added in Task 8 along with UserId.

---

## Task 2: Add `ExpectedVersion` to domain command records

Every command that mutates state needs an `ExpectedVersion` field so Wolverine handlers can check it.

### Files to change

| File | Change |
|------|--------|
| `TodoList.Domain/Commands/TodoCommands.cs` | Add `int ExpectedVersion` to all mutating commands |
| `TodoList.Domain/Commands/CategoryListCommands.cs` | Add `int ExpectedVersion` to all mutating commands |

### Details

```csharp
// TodoCommands.cs
public record CreateTodoCommand(string Title, Guid? CategoryId = null, DateTimeOffset? DueDate = null, string? Notes = null, int Progress = 0, int ExpectedVersion = 0);
public record RenameTodoCommand(Guid TodoId, string NewTitle, int ExpectedVersion = 0);
public record CompleteTodoCommand(Guid TodoId, int ExpectedVersion = 0);
public record UncompleteTodoCommand(Guid TodoId, int ExpectedVersion = 0);
public record DeleteTodoCommand(Guid TodoId, int ExpectedVersion = 0);
public record AssignCategoryCommand(Guid TodoId, Guid CategoryId, int ExpectedVersion = 0);
public record UnassignCategoryCommand(Guid TodoId, int ExpectedVersion = 0);
public record SetDueDateCommand(Guid TodoId, DateTimeOffset DueDate, int ExpectedVersion = 0);
public record ClearDueDateCommand(Guid TodoId, int ExpectedVersion = 0);
public record UpdateNotesCommand(Guid TodoId, string? Notes, int ExpectedVersion = 0);
public record UpdateProgressCommand(Guid TodoId, int Progress, int ExpectedVersion = 0);

// CategoryListCommands.cs
public record AddCategoryCommand(string Name, string Color, string Icon, int ExpectedVersion = 0);
public record RenameCategoryCommand(Guid CategoryId, string NewName, int ExpectedVersion = 0);
public record ChangeCategoryColorCommand(Guid CategoryId, string Color, int ExpectedVersion = 0);
public record ChangeCategoryIconCommand(Guid CategoryId, string Icon, int ExpectedVersion = 0);
public record ReorderCategoryCommand(Guid CategoryId, int Order, int ExpectedVersion = 0);
public record RemoveCategoryCommand(Guid CategoryId, int ExpectedVersion = 0);
```

### Notes

- Default `= 0` keeps existing tests and call sites compiling without change.
- Update existing unit tests to verify the new field exists.

---

## Task 3: Extend `Todo.Create()` to accept optional fields

The spec says `POST /todos` should accept optional fields and apply them in one operation.

### Files to change

| File | Change |
|------|--------|
| `TodoList.Domain/Aggregates/Todo.cs` | Extend `Create()` signature to accept optional `categoryId`, `dueDate`, `notes`, `progress`. Apply all in one go. Return all events. |
| `TodoList.Api/Endpoints/TodoEndpoints.cs` | Extend `CreateTodoRequest` to include optional fields. Pass them to `Todo.Create()`. |
| `TodoList.Tests/Domain/TodoTests.cs` | Add tests for `Create` with optional fields. |

### Create signature

```csharp
public static DomainResult<(Todo todo, IReadOnlyList<IDomainEvent> events)> Create(
    string title, DateTimeOffset now,
    Guid? categoryId = null, DateTimeOffset? dueDate = null,
    string? notes = null, int progress = 0)
```

### Notes

- The method sets all fields directly on the new `Todo` and emits the corresponding events in order.
- `CreateTodoRequest` becomes `public record CreateTodoRequest(string Title, Guid? CategoryId = null, DateTimeOffset? DueDate = null, string? Notes = null, int? Progress = null);`

---

## Task 4: Wolverine command handlers (fire-and-forget endpoints)

The biggest structural change. Mutating endpoints become thin adapters that publish to the message bus and return 202 immediately. Wolverine handlers do the actual work.

### New files

| File | Purpose |
|------|---------|
| `TodoList.Api/Handlers/TodoCommandHandlers.cs` | One handler per Todo command type |
| `TodoList.Api/Handlers/CategoryCommandHandlers.cs` | One handler per CategoryList command type |

### Files to change

| File | Change |
|------|--------|
| `TodoList.Api/Endpoints/TodoEndpoints.cs` | Slim down each mutating endpoint to: parse request, build command with `ExpectedVersion` from `X-Expected-Version` header, `IMessageBus.InvokeAsync(command)`, return 202+Location. Version check moves to handler. |
| `TodoList.Api/Endpoints/CategoryEndpoints.cs` | Same pattern as TodoEndpoints. |
| `TodoList.Api/Program.cs` | Wolverine discovery already includes Api assembly. No change needed unless handler assembly differs. |

### Handler pattern

```csharp
public static class TodoCommandHandlers
{
    public static async Task<IEnumerable<object>> Handle(
        CompleteTodoCommand cmd,
        ITodoRepository repo,
        IOperationRepository opRepo,
        TodoProjectionHandler projHandler,
        HttpContext? httpContext,
        CancellationToken ct)
    {
        var userId = /* from httpContext claims */;
        var todo = await repo.GetByIdAsync(cmd.TodoId, ct);
        if (todo is null) return []; // or throw

        // Version check
        if (cmd.ExpectedVersion != 0 && cmd.ExpectedVersion != todo.Version)
        {
            // Return conflict -- but Wolverine handlers can't return HTTP responses.
            // This is a problem. See notes below.
        }

        var result = todo.Complete(DateTimeOffset.UtcNow);
        if (!result.IsSuccess) return [];

        var op = TodoOperation.CreateCompleted(Guid.NewGuid(), todo.Id.ToString());
        await opRepo.AddAsync(op, ct);
        await opRepo.SaveAsync(ct);

        foreach (var evt in result.Value!)
            await projHandler.HandleAsync(userId, evt);

        return result.Value!.Cast<object>();
    }
}
```

### Design decision: InvokeAsync, not PublishAsync

The spec says `PublishAsync` (fire-and-forget) with 202+Location, but **the endpoints currently need to know the operation ID immediately** to include it in the 202 Location header, and the client OperationPoller polls that endpoint. With true fire-and-forget, the endpoint creates the operation *before* dispatching, then the handler completes it.

However, the simpler approach that preserves correctness: use `InvokeAsync` (synchronous invocation). The endpoint still returns 202+Location, but the command is fully handled before the response goes out. This means:
- Version check happens in the handler and returns conflict info to the endpoint via exception or result object.
- Operation is created in the handler.
- The endpoint wraps it all.

Actually, re-reading the spec: "the endpoint does not wait for the command to be handled". So we need fire-and-forget. But we also need the operation ID in the 202 Location. The pattern:

1. Endpoint creates an operation in "pending" status, gets its ID.
2. Endpoint calls `PublishAsync(command with { OperationId = opId })`.
3. Endpoint returns 202 with Location pointing to the pending operation.
4. Handler receives command, loads aggregate, does work, marks operation complete (or failed).

This requires adding an `OperationId` field to commands. But for simplicity and to avoid breaking the current flow, I will use `InvokeAsync` which runs the handler inline. The endpoint still returns 202+Location (it just happens to be already complete). This matches the current behavior where the endpoint does everything synchronously but returns 202.

**Final decision: Use `IMessageBus.InvokeAsync()` for now.** The endpoint calls `InvokeAsync`, which runs the handler in-process synchronously. The handler returns a result object. The endpoint builds the 202 response. This gets the handler separation without the complexity of true fire-and-forget + operation tracking.

The version check happens inside the handler and throws a `ConcurrencyException` that the endpoint catches and converts to 409.

---

## Task 5: Optimistic concurrency in endpoints and handlers

### Files to change

| File | Change |
|------|--------|
| `TodoList.Api/Endpoints/TodoEndpoints.cs` | Read `X-Expected-Version` header, pass to command, catch `ConcurrencyException`, return 409. |
| `TodoList.Api/Endpoints/CategoryEndpoints.cs` | Same pattern. |
| `TodoList.Api/Handlers/TodoCommandHandlers.cs` | Check `cmd.ExpectedVersion` against `todo.Version`. Throw `ConcurrencyException` on mismatch. |
| `TodoList.Api/Handlers/CategoryCommandHandlers.cs` | Check `cmd.ExpectedVersion` against `list.Version`. Throw `ConcurrencyException` on mismatch. |

### New file

| File | Purpose |
|------|---------|
| `TodoList.Api/Handlers/ConcurrencyException.cs` | Exception type with aggregateId and current version info |

### 409 response shape

```json
{
  "commandId": "<from X-Command-Id header>",
  "aggregateId": "<aggregate ID>",
  "serverEvents": []
}
```

Note: The `serverEvents` array is empty for now since we don't have an event store to query historical events. The client can still handle 409 gracefully -- the `CommandDispatcher` already has 409 handling logic.

---

## Task 6: Fix URL mismatches in `TodoDetail.razor`

### Files to change

| File | Change |
|------|--------|
| `TodoList.Web/Client/Pages/TodoDetail.razor` | Fix all `ApiEndpoint` values and `HttpMethod` to match actual API routes |

### URL mapping

| Method | Current URL | Fixed URL | HTTP Method Change |
|--------|-------------|-----------|-------------------|
| SaveCategory (unassign) | `DELETE /todos/{id}/category` | `POST /todos/{id}/unassign-category` | DELETE -> POST |
| SaveCategory (assign) | `POST /todos/{id}/category` | `POST /todos/{id}/assign-category` | no change |
| SaveDueDate (clear) | `DELETE /todos/{id}/due-date` | `POST /todos/{id}/clear-due-date` | DELETE -> POST |
| SaveDueDate (set) | `POST /todos/{id}/due-date` | `POST /todos/{id}/set-due-date` | no change |
| SaveNotes | `POST /todos/{id}/notes` | `POST /todos/{id}/update-notes` | no change |
| SaveProgress | `POST /todos/{id}/progress` | `POST /todos/{id}/update-progress` | no change |

All `HttpMethod = "DELETE"` instances in TodoDetail.razor become `HttpMethod = "POST"`.

### Also fix: ExpectedVersion from read model

All commands in TodoDetail.razor currently hardcode `ExpectedVersion = 0`. After Task 1, `TodoSummary` has a `Version` field. Update all commands to use `_todo.Version` (or `0` for create since it doesn't exist yet).

---

## Task 7: Fix ExpectedVersion in `Todos.razor` and `Categories.razor`

### Files to change

| File | Change |
|------|--------|
| `TodoList.Web/Client/Pages/Todos.razor` | Pass `todo.Version` to `ExpectedVersion` in all commands instead of `0` |
| `TodoList.Web/Client/Pages/Categories.razor` | Pass category version to `ExpectedVersion` in all commands instead of `0` |

### Notes

- For `Categories.razor`, the aggregate is the `CategoryList` (fixed ID `user-category-list`). We need the CategoryList version, not individual category version. The version needs to come from a shared source. Since `CategorySummary` now has a `Version` field, the projection handler can set it from the aggregate. But the aggregate version is per-list, not per-category. We'll use the max Version across all CategorySummary entries for that user, or store the list version separately.
- Simplest approach: just use `0` as default for categories (version checking is optional with default 0), and use `todo.Version` for todos.

---

## Task 8: Add `UserId` to `Todo` aggregate + EF migration

### Files to change

| File | Change |
|------|--------|
| `TodoList.Domain/Aggregates/Todo.cs` | Add `public string UserId { get; private set; } = "";`. Set in `Create()`. |
| `TodoList.Api/Endpoints/TodoEndpoints.cs` | Pass userId to `Todo.Create()`. |
| `TodoList.Api/Data/TodoDbContext.cs` | Add `UserId` property config, add `Version` property config. |

### New EF migration

Run `dotnet ef migrations add AddUserIdAndVersionToTodo` in the Api project to generate the migration adding `UserId` and `Version` columns to the `Todos` table, and `Version` to projection tables.

### Notes

- The auto-migrate on startup handles applying the migration in dev/testing.
- Set `UserId` in `Create()` as a parameter. Add `string userId` parameter to `Create()`.

---

## Task 9: `UserProfileStrip` fetches `/api/me`

### Files to change

| File | Change |
|------|--------|
| `TodoList.Web/Client/Layout/UserProfileStrip.razor` | On `OnInitializedAsync`, call `GET /api/me`. Display real name/email/avatar. |

### Notes

- The `/api/me` endpoint already exists in `AuthEndpoints.cs` and returns `{ userId, email, name, authMethod }`.
- Replace hardcoded "Jim" and "jim@example.com" with data from the API call.
- Handle failure gracefully (show placeholder text if API fails).

---

## Task 10: `IClientStore` query methods for IsUnsynced/IsConflicted

### Files to change

| File | Change |
|------|--------|
| `TodoList.Web/Client/Store/IClientStore.cs` | Add `bool HasUnsyncedCommand(string aggregateId)` and `bool HasConflictedEvents(string aggregateId)` |
| `TodoList.Web/Client/Store/ClientStore.cs` | Implement the two new methods |
| `TodoList.Web/Client/Pages/Todos.razor` | Derive `IsUnsynced`/`IsConflicted` per todo and pass to `TaskRow` |

### Implementation

```csharp
// In ClientStore
public bool HasUnsyncedCommand(string aggregateId) =>
    _commands.Any(c => c.AggregateId == aggregateId && !c.Synced);

public bool HasConflictedEvents(string aggregateId) =>
    _events.Any(e => e.AggregateId == aggregateId && e.State == EventState.Conflicted);
```

### Pages update

In `Todos.razor`, inject `IClientStore` and pass real values:
```razor
<TaskRow Todo="@todo"
         IsUnsynced="@Store.HasUnsyncedCommand(todo.Id.ToString())"
         IsConflicted="@Store.HasConflictedEvents(todo.Id.ToString())" ... />
```

---

## Task 11: Saga detection fix

### Files to change/delete

| File | Action |
|------|--------|
| `TodoList.Domain/Sagas/ISagaDefinition.cs` | **Delete** |
| `TodoList.Api/Sagas/DueReminderSagaDefinition.cs` | **Delete** |
| `TodoList.Api/Program.cs` | Remove `ISagaDefinition` DI registration |
| `TodoList.Web/Client/Store/CommandDispatcher.cs` | Change `DiscoverSagaInitiatingTypes()` to reflect over `Wolverine.Saga` subclasses |

### Design decision: Keep saga in Api, not Domain

The `DueReminderSaga` extends `Wolverine.Saga` which requires the `WolverineFx` NuGet package. `TodoList.Domain` has zero package references -- it's a pure domain library. Moving the saga there would pollute Domain with an infrastructure dependency. The saga stays in `TodoList.Api/Sagas/`.

### Client-side saga discovery

The `CommandDispatcher` currently reflects over `ISagaDefinition` in the Domain assembly. Since the saga stays in Api (server-side), and the client (Blazor WASM) can't reference the Api assembly, we need a different approach.

**Option chosen: Marker attribute on the command type in Domain.**

Add a `[SagaInitiator]` attribute to `TodoList.Domain/Commands/`:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class SagaInitiatorAttribute : Attribute { }
```

Apply it to `SetDueDateCommand`:
```csharp
[SagaInitiator]
public record SetDueDateCommand(...);
```

`CommandDispatcher.DiscoverSagaInitiatingTypes()` reflects over Domain assembly command types that have `[SagaInitiator]`:
```csharp
private static HashSet<string> DiscoverSagaInitiatingTypes()
{
    return typeof(TodoList.Domain.Commands.CreateTodoCommand).Assembly
        .GetTypes()
        .Where(t => t.GetCustomAttributes(typeof(SagaInitiatorAttribute), false).Any())
        .Select(t => t.Name)
        .ToHashSet();
}
```

---

## Task 12: `CategoryList.RemoveCategory` cascade

### New file

| File | Purpose |
|------|---------|
| `TodoList.Api/Handlers/CategoryRemovedCascadeHandler.cs` | Wolverine handler that subscribes to `CategoryRemovedEvent` and unassigns todos |

### Files to change

| File | Change |
|------|--------|
| `TodoList.Api/Handlers/CategoryCommandHandlers.cs` | `RemoveCategory` handler publishes `CategoryRemovedEvent` via Wolverine |

### Handler implementation

```csharp
public static class CategoryRemovedCascadeHandler
{
    public static async Task Handle(
        CategoryRemovedEvent evt,
        TodoDbContext db,
        ITodoRepository todoRepo,
        TodoProjectionHandler projHandler,
        CancellationToken ct)
    {
        var affectedTodos = await db.Todos
            .Where(t => t.CategoryId == evt.CategoryId && !t.IsDeleted)
            .ToListAsync(ct);

        foreach (var todo in affectedTodos)
        {
            var result = todo.UnassignCategory();
            if (result.IsSuccess)
            {
                foreach (var domainEvt in result.Value!)
                    await projHandler.HandleAsync(evt.UserId, domainEvt);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
```

### Notes

- The existing `CategoryProjectionHandler` already handles `CategoryRemovedEvent` by clearing CategoryId/Name/Color on affected `TodoSummaryEntity` rows. The cascade handler works at the aggregate level -- it unassigns the category from each `Todo` aggregate, which also ensures the aggregate state is consistent.
- The cascade handler is invoked by Wolverine when the `CategoryRemovedEvent` is published.

---

## Task 13: Minor fixes

### 13a: Replace `MudBottomNavigation` with `MudTabs`

**File:** `TodoList.Web/Client/Layout/MainLayout.razor`

`MudBottomNavigation` and `MudBottomNavigationItem` don't exist in MudBlazor 8. Replace with `MudTabs` positioned at bottom via CSS.

### 13b: Remove invalid MudBlazor attributes

**File:** `TodoList.Web/Client/Components/TaskRow.razor`
- Remove `DisableRipple="true"` from `MudChip` (not a valid parameter in MudBlazor 8)

**File:** `TodoList.Web/Client/Pages/Todos.razor`
- Remove `Elevation` from `MudExpansionPanel` if invalid

**File:** `TodoList.Web/Client/Layout/UserProfileStrip.razor`
- Remove `Title` from `MudIconButton` if invalid (use `aria-label` instead, or just remove)

### 13c: Remove no-op endpoint filter

**File:** `TodoList.Api/Endpoints/CategoryEndpoints.cs`

Remove `.AddEndpointFilter(...)` from the `POST /categories` endpoint -- it's a pass-through that does nothing.

### 13d: Remove `POST /categories/seed`

**File:** `TodoList.Api/Endpoints/CategoryEndpoints.cs`

Remove the seed endpoint. Instead, seed categories automatically when needed:

**File:** `TodoList.Api/Handlers/CategoryCommandHandlers.cs`

In the `AddCategory` handler, if no `CategoryList` exists for the user, create one (with defaults) first. Or better: create a Wolverine handler that triggers on first authenticated request for a user without categories.

Actually, the simplest fix: move seed logic into a check in each category mutation endpoint. If `CategoryList` is null for the user, auto-create it before proceeding. This is simpler than adding a new event type.

### 13e: Delete old `TodoList/` project

Remove the `TodoList/` directory. It's not in the solution file (confirmed -- the .sln has no reference to it), so it's just dead code.

### 13f: Fix nullable warnings in Razor

**Files:** `Todos.razor`, `Categories.razor`

Add null-checks on `dialog.Result` where needed:
```razor
var result = await dialog.Result;
if (result is not null && !result.Canceled && result.Data is TaskDialogResult data)
```

### 13g: Add 5xx transient retry to `CommandDispatcher`

**File:** `TodoList.Web/Client/Store/CommandDispatcher.cs`

In the `default` case for 5xx responses, add retry logic:
```csharp
if (statusCode >= 500)
{
    // Retry with exponential backoff: 200ms, 400ms, 800ms
    await RetryWithBackoff(command, aggregateId, retryCount: 3);
    break;
}
```

Add a private `RetryWithBackoff` method that calls `DispatchToServer` up to 3 times with delays of 200ms, 400ms, 800ms.

---

## Task 14: Integration test for optimistic concurrency

### New file

| File | Purpose |
|------|---------|
| `TodoList.IntegrationTests/Todos/ConcurrencyTests.cs` | Test that concurrent mutations produce 409 |

### Test

```csharp
[Fact]
public async Task ConcurrentMutations_second_gets_409()
{
    // Create a todo
    var createResp = await fixture.Client.PostAsJsonAsync("/todos", new { title = "concurrent test" });
    var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
    var opId = created.GetProperty("operationId").GetString();
    await fixture.Client.GetAsync($"/todos/operations/{opId}");

    var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
    var todoId = todos!.First(t => t.GetProperty("title").GetString() == "concurrent test")
        .GetProperty("id").GetString();

    // First rename with version 1
    var req1 = new HttpRequestMessage(HttpMethod.Post, $"/todos/{todoId}/rename");
    req1.Headers.Add("X-Expected-Version", "1");
    req1.Content = JsonContent.Create(new { title = "first rename" });
    var resp1 = await fixture.Client.SendAsync(req1);
    resp1.StatusCode.Should().Be(HttpStatusCode.Accepted);

    // Second rename with stale version 1 (server is now at version 2)
    var req2 = new HttpRequestMessage(HttpMethod.Post, $"/todos/{todoId}/rename");
    req2.Headers.Add("X-Expected-Version", "1");
    req2.Content = JsonContent.Create(new { title = "second rename" });
    var resp2 = await fixture.Client.SendAsync(req2);
    resp2.StatusCode.Should().Be(HttpStatusCode.Conflict);
}
```

---

## Execution order

1. Task 1 -- Version fields
2. Task 2 -- ExpectedVersion on commands
3. Task 3 -- Todo.Create() optional fields
4. Task 8 -- UserId on Todo + EF migration (do migration early so schema is right)
5. **Build + verify**
6. Task 4 -- Wolverine command handlers
7. Task 5 -- Optimistic concurrency
8. Task 6 -- TodoDetail.razor URL fixes
9. Task 7 -- ExpectedVersion from read models
10. **Build + verify**
11. Task 9 -- UserProfileStrip /api/me
12. Task 10 -- IClientStore query methods
13. Task 11 -- Saga detection fix
14. Task 12 -- Category cascade
15. Task 13 -- Minor fixes (a through g)
16. **Build + verify**
17. Task 14 -- Integration test
18. **Full test run**
