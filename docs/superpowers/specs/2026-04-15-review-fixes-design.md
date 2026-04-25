# Review Fixes Design

**Date:** 2026-04-15
**Status:** Approved
**Purpose:** Fix all issues identified in the code review — correctness gaps, spec deviations, and minor cleanup. One spec, one plan, verified end-to-end.

---

## 1. Optimistic Concurrency (end-to-end, async)

The mutation pipeline is async: every mutating endpoint accepts the request, enqueues a Wolverine command, and returns 202 + a Location header pointing at the operation resource. The concurrency check happens *inside* the command handler, not at the endpoint, so conflicts surface on the operation poll — not as a synchronous 409.

### Server

Every mutating endpoint reads `X-Expected-Version` from the request header and forwards it on the command (`ExpectedVersion`). The endpoint always returns:

```
202 Accepted
Location: /todos/operations/{operationId}
{ "operationId": "...", "retryAfterMs": 200 }
```

The Wolverine command handler loads the aggregate, compares `cmd.ExpectedVersion` to `aggregate.Version`, and on mismatch marks the operation `Status = "failed"` with:

```
FailureCode = "VERSION_CONFLICT"
FailureReason = "Version conflict: expected version does not match server version {N}"
```

The client polls `GET /todos/operations/{id}` and sees:

```json
{
  "id": "...",
  "status": "failed",
  "failureCode": "VERSION_CONFLICT",
  "failureReason": "Version conflict: expected version does not match server version 7",
  ...
}
```

The reconciliation events arrive separately via SignalR push (same as any other server-side mutation) — the client's `EventHubClient` calls `ReplaceSpeculative` on the affected aggregate, which clears the failed optimistic write and applies the authoritative state.

`FailureCode` is a known string constant: `VERSION_CONFLICT`, `VALIDATION_ERROR`, `NOT_FOUND`, or `INTERNAL_ERROR`. The client branches on this value — no substring matching of `FailureReason`.

Both `Todo` and `CategoryList` aggregates already track `Version`.

### Client

`CommandDispatcher` sends `X-Expected-Version` on every command. After receiving 202, it polls the operation resource via `OperationPoller`. On a `failed` terminal state with `FailureCode = "VERSION_CONFLICT"`, it calls `_store.DiscardSpeculative(aggregateId)` and `_store.MarkSynced(commandId)`, then surfaces a snackbar warning. The SignalR push delivering the authoritative server events triggers `ReplaceSpeculative`, reconciling the local store.

`ExpectedVersion` on dispatched commands reflects the actual aggregate version from the read model (`TodoSummary.Version` for todos, `CategoryListSummary.Version` for category mutations) — not the hardcoded `0` previously used in pages.

### Verification

Integration test: two concurrent mutations to the same aggregate. The second mutation's operation poll returns `status = "failed"` with `failureCode = "VERSION_CONFLICT"`. Subsequent SignalR push for the first mutation arrives and the client's store reconciles via `ReplaceSpeculative`.

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

The `Todo.Create()` method accepts all optional fields directly and applies them in one operation — no separate commands dispatched. One command in, one aggregate created with all fields set, one set of events out.

---

## 3. Wolverine Dispatch

Every mutating endpoint calls `IMessageBus.PublishAsync(command)` and returns 202+Location immediately — the endpoint does not wait for the command to be handled. The endpoint is a thin adapter: parse request → build command → publish to bus → return 202+Location.

Wolverine command handlers live in `TodoList.Api/Handlers/` — one handler per command type. Each handler loads the aggregate, calls the domain method, saves, and publishes the resulting domain events to Wolverine. Projection updates happen in separate event handlers that subscribe to those domain events — command handlers do not update projections directly.

GET endpoints stay as direct projection queries — no bus needed for reads.

The existing `DueReminderSaga` fires naturally because domain events flow through Wolverine after the command handler publishes them.

---

## 4. Saga Detection Fix

`DueReminderSaga` stays in `TodoList.Api/Sagas/`. The `Wolverine.Saga` base class is a server-side persistence concern, so keeping it in the Api keeps `TodoList.Domain` free of `WolverineFx` (and keeps the Blazor WASM client's transitive package graph small — it references Domain).

Saga discovery is attribute-based rather than reflective over `Saga<T>` subclasses:

- A new `[SagaInitiator]` attribute lives in `TodoList.Domain/Sagas/` — just a marker, no WolverineFx dependency
- Domain events that initiate a saga are decorated with it (e.g. `[SagaInitiator] record TodoDueDateSetEvent(...)`)
- Client's `CommandDispatcher` reflects over `TodoList.Domain` assembly → finds types with `[SagaInitiator]` → strips `"Event"` suffix → shows saga toast for matching command types
- Server's Wolverine discovers sagas normally by scanning the Api assembly (no special config needed)

Delete `ISagaDefinition` and `DueReminderSagaDefinition`. They're unnecessary indirection — the attribute on the triggering event is enough.

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
