# Review Fixes Design

**Date:** 2026-04-15
**Status:** Approved
**Purpose:** Fix all issues identified in the code review — correctness gaps, spec deviations, and minor cleanup. One spec, one plan, verified end-to-end.

---

## 1. Optimistic Concurrency (end-to-end)

### Server

Every mutating endpoint in `TodoEndpoints.cs` and `CategoryEndpoints.cs` reads the `X-Expected-Version` request header. After loading the aggregate, the handler compares the header value against the aggregate's current `Version`. If they don't match, the endpoint returns:

```
409 Conflict
{
  "commandId": "<from request>",
  "aggregateId": "<aggregate ID>",
  "serverEvents": [{ "id", "aggregateId", "aggregateVersion", "type", "payload", "timestamp" }]
}
```

The `serverEvents` array contains all events since the client's expected version, fetched from the event/projection store.

Both `Todo` and `CategoryList` aggregates already track `Version`.

### Client

`CommandDispatcher` already sends `X-Expected-Version` and handles 409/422 responses. The fix is ensuring `ExpectedVersion` on dispatched commands reflects the actual aggregate version from the read model, not the hardcoded `0` currently used in all pages.

`TodoSummary` and `CategorySummary` must carry a `Version` field so pages can pass it to commands.

### Verification

Integration test: two concurrent mutations to the same aggregate — second gets 409 with server events in the response body.

---

## 2. URL Mismatches + POST /todos Optional Fields

### URL fixes in TodoDetail.razor

All dispatched URLs must match the API's intent-specific POST routes:

| Current (broken)                | Fixed                                    |
|---------------------------------|------------------------------------------|
| `POST /todos/{id}/category`    | `POST /todos/{id}/assign-category`       |
| `DELETE /todos/{id}/category`  | `POST /todos/{id}/unassign-category`     |
| `POST /todos/{id}/due-date`   | `POST /todos/{id}/set-due-date`          |
| `DELETE /todos/{id}/due-date`  | `POST /todos/{id}/clear-due-date`        |
| `POST /todos/{id}/notes`      | `POST /todos/{id}/update-notes`          |
| `POST /todos/{id}/progress`   | `POST /todos/{id}/update-progress`       |

All methods become POST. The `HttpMethod` field on `ClientCommand` changes accordingly — no DELETE used for sub-resource commands.

### POST /todos optional fields

Extend `CreateTodoRequest` to:

```csharp
public record CreateTodoRequest(
    string Title,
    Guid? CategoryId = null,
    DateTimeOffset? DueDate = null,
    string? Notes = null,
    int? Progress = null);
```

If optional fields are provided, the Wolverine command handler dispatches the additional domain methods (`AssignCategory`, `SetDueDate`, `UpdateNotes`, `UpdateProgress`) after `Todo.Create()` in the same operation.

---

## 3. Wolverine Dispatch

Every mutating endpoint calls `IMessageBus.InvokeAsync<T>(command)` instead of loading aggregates directly. The endpoint becomes a thin adapter: parse request → build command → `bus.InvokeAsync` → return 202+Location.

Wolverine command handlers live in `TodoList.Api/Handlers/` — one handler per command type. Each handler loads the aggregate, calls the domain method, saves, updates projections, and returns the operation result.

GET endpoints stay as direct projection queries — no bus needed for reads.

The existing `DueReminderSaga` fires naturally because events flow through Wolverine after the command handler completes.

---

## 4. Saga Detection Fix

Move both `DueReminderSagaDefinition` and `DueReminderSaga` to `TodoList.Domain/Sagas/`. The saga is pure domain logic (state transitions, message routing) with no server dependencies. `NotificationHandlers` stays in `TodoList.Api` — it does the actual I/O.

- Client reflects over `TodoList.Domain` assembly → finds `DueReminderSagaDefinition` → shows saga toast
- Server's Wolverine scans `TodoList.Domain` assembly → finds `DueReminderSaga` → runs it

---

## 5. CategoryList.RemoveCategory Cascade

The `RemoveCategoryCommand` Wolverine handler removes the category from the `CategoryList` aggregate and publishes `CategoryRemovedEvent` through Wolverine.

A new `CategoryRemovedCascadeHandler` subscribes to `CategoryRemovedEvent`. It queries todos with that `CategoryId`, calls `UnassignCategory()` on each, saves them. Each `TodoCategoryUnassignedEvent` flows through Wolverine, updating the `TodoSummary` projections (clearing `CategoryId`, `CategoryName`, `CategoryColor`).

This keeps it event-driven — no command handler reaching across aggregate boundaries.

---

## 6. Todo.UserId + Client Wiring Fixes

### Todo.UserId

Add `UserId` (string) to the `Todo` aggregate, set during `Create()`. Endpoints pass the authenticated user's ID. New EF migration adds the column to the `Todos` table.

### /api/me on startup

`UserProfileStrip` fetches `GET /api/me` on initialization. Displays real name, email, and avatar URL instead of hardcoded "Jim" / "jim@example.com".

### IsUnsynced / IsConflicted

Pages derive these from `ClientStore` state and pass them to `TaskRow`:

- **IsUnsynced:** true if there's an unsynced command for this aggregate ID (command where `Synced == false`)
- **IsConflicted:** true if the aggregate has events with `EventState.Conflicted`

`IClientStore` needs two query methods: `HasUnsyncedCommand(string aggregateId)` and `HasConflictedEvents(string aggregateId)`.

### ExpectedVersion from read model

`TodoSummary` and `CategorySummary` carry a `Version` field. Pages pass this to `ClientCommand.ExpectedVersion` instead of hardcoding `0`.

---

## 7. Minor Fixes

| Issue | Fix |
|---|---|
| `MudBottomNavigation` doesn't exist in MudBlazor 8 | Replace with `MudTabs` positioned at bottom |
| MudBlazor API warnings (`Elevation` on `MudExpansionPanel`, `DisableRipple` on `MudChip`, `Title` on `MudIconButton`) | Remove invalid attributes |
| No-op endpoint filter on `POST /categories` | Remove it |
| `POST /categories/seed` not in spec | Remove; seed on first login via Wolverine handler reacting to user creation |
| Old `TodoList/` stub project in repo | Delete it, remove from solution |
| Nullable reference warnings in Razor files | Null-check `dialog.Result` |
| No 5xx transient retry in `CommandDispatcher` | Add 3x exponential backoff: 200ms, 400ms, 800ms |
