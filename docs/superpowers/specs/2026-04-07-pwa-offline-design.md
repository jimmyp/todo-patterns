# PWA / Offline Design

**Date:** 2026-04-07
**Status:** Approved
**Purpose:** Define the event-sourced client architecture that powers both online and offline operation of the Blazor WASM app. The same flow handles both cases — offline just means server confirmation is delayed.

---

## 1. Core Principle

There is **one flow** for all mutations, online or offline:

1. User action → command written to `ClientStore` with a speculative event
2. Read models rebuilt from store immediately — UI updates
3. Command sent to API (online) or queued (offline)
4. Server confirms → speculative event replaced with confirmed server event → read models rebuilt
5. On conflict → server events win → conflicting speculative event removed → redo toast shown

No separate "offline mode". No spinners for pending commands while offline. A subtle unsynced dot on affected items indicates local-only state.

---

## 2. Shared Domain Project

All domain types live in `TodoList.Domain` and are referenced by both the API and the Blazor WASM client. See Domain Model Extension spec for full details.

The client uses `TodoList.Domain` for:
- **Validation** — runs the same rules as the server before dispatching, no duplication
- **Projection** — uses the same projector logic to build local read models from events
- **Saga trigger detection** — reflects over `ISagaDefinition` implementations at runtime to know which commands initiate server-side background work

---

## 3. Event and Command Model

### Event

```csharp
public record ClientEvent
{
    public string Id { get; init; }               // client-generated UUID
    public string AggregateId { get; init; }
    public int AggregateVersion { get; init; }    // position within this aggregate's history
    public string Type { get; init; }
    public object Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public EventSource Source { get; init; }      // Client | Server
    public EventState State { get; init; }        // Speculative | Confirmed | Conflicted
}

public enum EventSource { Client, Server }
public enum EventState { Speculative, Confirmed, Conflicted }
```

`Conflicted` means the server returned 422 — the event is held pending user correction. No invalid combinations are possible.

### Command

```csharp
public record ClientCommand
{
    public string Id { get; init; }               // client-generated UUID
    public string AggregateId { get; init; }
    public int ExpectedVersion { get; init; }     // current known version of the aggregate
    public int SpeculativeVersion { get; init; }  // ExpectedVersion + 1
    public string Type { get; init; }
    public object Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public bool Synced { get; init; }             // false = not yet confirmed by server
}
```

### Event and command types

Defined in `TodoList.Domain` — see Domain Model Extension spec for full lists.

---

## 4. ClientStore

`localStorage`-backed store. Holds both the event log (keyed by `aggregateId`) and the pending command queue.

```csharp
public interface IClientStore
{
    void AppendEvent(ClientEvent evt);
    IReadOnlyList<ClientEvent> GetEventsFor(string aggregateId);
    IReadOnlyList<ClientEvent> GetAllEvents();              // ordered by (aggregateId, aggregateVersion)
    void ReplaceSpeculative(string aggregateId, IReadOnlyList<ClientEvent> serverEvents);
    void MarkConflicted(string aggregateId, IReadOnlyList<ValidationError> errors);
    void DiscardSpeculative(string aggregateId);
    IReadOnlyList<ClientCommand> GetUnsyncedCommands();
    void MarkSynced(string commandId);
}
```

On append, immediately triggers a read model rebuild for the affected aggregate.

---

## 5. Read Model Projectors

Two projectors replay events to build local read models from `ClientStore`. Rebuilt from scratch after every merge. Projector logic is shared from `TodoList.Domain`.

### LocalTodoStore

Projects events into `TodoSummary[]`. Applied in `AggregateVersion` order per todo.

```csharp
public interface ILocalTodoStore
{
    IReadOnlyList<TodoSummary> Todos { get; }
    TodoSummary? GetById(string id);
}
```

### LocalCategoryStore

Projects events into `CategorySummary[]`.

```csharp
public interface ILocalCategoryStore
{
    IReadOnlyList<CategorySummary> Categories { get; }
    CategorySummary? GetById(string id);
}
```

UI components bind to these stores only. No component ever calls the API for reads.

---

## 6. Command Dispatch Flow

```
User action
  ↓
CommandDispatcher.Dispatch(command)
  ↓
1. Validate using TodoList.Domain validators — return errors immediately if invalid
2. Write command to ClientStore (Synced: false)
3. Write speculative event (State: Speculative, Source: Client)
4. Rebuild affected read model
5. Notify UI (reactive store update)
  ↓
  [if online]                        [if offline]
  ↓                                  ↓
  [if saga-initiating command]       Command stays in store (Synced: false)
  ↓                                  UI shows unsynced dot on item
  Show toast:                        (no spinner)
  "X will begin when back online"
  ↓
POST to API endpoint
  with X-Expected-Version header
  ↓
202 Accepted + Location header
  ↓
Poll GET /operations/{id}
  (until status != pending/processing)
  ↓
200 { status: "complete", event: ClientEvent }
  ↓
ClientStore.ReplaceSpeculative(aggregateId, [confirmedEvent])
  ↓
Rebuild read model → remove unsynced dot
```

### Saga-initiating commands (offline)

`CommandDispatcher` reflects over `ISagaDefinition` implementations in `TodoList.Domain` at startup to build a set of saga-initiating command types. When offline and the command is in this set, a toast is shown: *"[Action] will begin when you're back online."* The command is still queued and dispatched normally on reconnect — the server starts the saga from the command.

### On reconnect

`ConnectivityService` fires `OnConnectivityChanged(true)`. `SyncService` picks up all unsynced commands from `ClientStore` and dispatches them in `Timestamp` order.

---

## 7. Conflict Resolution (409)

A version conflict occurs when the server rejects a command because `ExpectedVersion` does not match the server's current version for that aggregate.

```
Server response: 409 Conflict
  body: { commandId, aggregateId, serverEvents: ClientEvent[] }
  ↓
ClientStore.ReplaceSpeculative(aggregateId, serverEvents)
  ↓
Rebuild read model from merged store (server events win)
  ↓
Show MudSnackbar toast:
  "[Action] on '[title]' was overridden by a newer change."
  [Redo] → re-queues original command with updated ExpectedVersion
```

Server events are inserted at their correct `AggregateVersion` positions. The client's speculative event is removed. The merged store is replayed to rebuild the read model.

---

## 8. Validation Conflict (422)

```
Server response: 422 Unprocessable Entity
  body: { commandId, errors: [{ field, message }] }
  ↓
ClientStore.MarkConflicted(aggregateId, errors)
  ↓
Affected item shows warning icon (distinct from unsynced dot)
  ↓
Show MudSnackbar toast:
  "'[title]' couldn't be saved — [message]. [Review]"
  [Review] → navigate to item/form, errors shown inline
  ↓
User corrects and resubmits → new command with corrected payload + current ExpectedVersion
  OR
User cancels → ClientStore.DiscardSpeculative(aggregateId) → rebuild read model → clear warning
```

---

## 9. ConnectivityService

```csharp
public interface IConnectivityService
{
    bool IsOnline { get; }
    event Action<bool> OnConnectivityChanged;
}
```

Implemented via JS interop: listens to `window` `online` / `offline` events and polls `navigator.onLine`.

Used by:
- `CommandDispatcher` — immediate dispatch vs. queue; saga toast when offline
- `TaskRow` — spinner (online, pending) vs. unsynced dot (offline, pending)
- `SyncService` — triggered on `OnConnectivityChanged(true)`

---

## 10. Other Failure Handling

| Failure type | Behaviour |
|---|---|
| Transient (5xx, network error) | Retry up to 3× with exponential backoff (200ms, 400ms, 800ms) |
| Not found (404) on delete/complete | Treat as success — aggregate already gone; discard speculative event, rebuild read model |
| Auth (401) | Redirect to `/login` |
| Other 4xx | Discard speculative event, rebuild read model, show error snackbar |
| All retries exhausted | Persistent `MudAlert` (error severity): "Some changes couldn't sync. [Retry]" |

---

## 11. Server-Initiated Events — Wolverine, SignalR, Push

Background work (reminders, scheduled processing, email) runs server-side via Wolverine sagas. The client never runs sagas — even when offline, it queues the initiating command and lets the server start the saga on reconnect.

### How the client learns about server-initiated events

| Client state | Delivery mechanism |
|---|---|
| App open, online | SignalR — server pushes `ClientEvent` to the connected client hub |
| App closed, mobile | Push API — server sends a push notification via the browser Push API |
| App closed, desktop | No delivery — client picks up missed events on next open via `GET /todos` / `GET /categories` seeding `ClientStore` on startup |

### Wolverine saga definition

Sagas implement `ISagaDefinition` (defined in `TodoList.Domain`):

```csharp
public interface ISagaDefinition
{
    Type InitiatingCommandType { get; }
    string Description { get; }
}
```

The API registers all saga definitions with Wolverine at startup. `CommandDispatcher` in the client reflects over `ISagaDefinition` implementations from the shared domain assembly to detect saga-initiating commands.

### SignalR hub

```
Hub: /hubs/events
  Server → Client: ReceiveEvent(ClientEvent event)
```

Client subscribes on startup (when authenticated). On receiving an event, `ClientStore.AppendEvent` is called and affected read models are rebuilt — same path as any other event.

---

## 12. PWA Service Worker

Blazor PWA template generates `service-worker.published.js`. Caching strategy:

| Asset type | Strategy |
|---|---|
| App shell (WASM, JS, CSS, fonts) | Cache-first, versioned. Updated on deploy via Blazor's asset manifest. |
| API calls (`/api/*`) | Network-only. Read models are local; the app never needs API reads to render. |
| Auth endpoints (`/auth/*`) | Network-only. Never cached. |

No stale API data is ever served from cache — `ClientStore` is the cache.

---

## 13. PWA Manifest

```json
{
  "name": "TodoList",
  "short_name": "Todos",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#0B0D10",
  "theme_color": "#8B5CF6",
  "icons": [
    { "src": "icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "icon-512.png", "sizes": "512x512", "type": "image/png" }
  ]
}
```
