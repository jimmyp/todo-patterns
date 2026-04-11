# PWA / Offline Design

**Date:** 2026-04-07
**Status:** Approved
**Purpose:** Define the event-sourced client architecture that powers both online and offline operation of the Blazor WASM app. The same flow handles both cases — offline just means server confirmation is delayed.

---

## 1. Core Principle

There is **one flow** for all mutations, online or offline:

1. User action → command written to `ClientEventLog` with a speculative event
2. Read models rebuilt from log immediately — UI updates
3. Command sent to API (online) or queued (offline)
4. Server confirms → speculative event replaced with confirmed server event → read models rebuilt
5. On conflict → server events win → conflicting speculative event removed → redo toast shown

No separate "offline mode". No spinners for pending commands while offline. A subtle unsynced dot on affected items indicates local-only state.

---

## 2. Event and Command Model

### Event

```typescript
{
  id: string                  // client-generated UUID
  aggregateId: string         // todo ID or category ID
  aggregateVersion: number    // position within this aggregate's history
  type: EventType
  payload: object
  timestamp: ISO8601
  source: "client" | "server"
  confirmed: boolean          // false = speculative
  conflicted: boolean         // true = server returned 422, awaiting user correction
}
```

### Command

```typescript
{
  id: string                  // client-generated UUID
  aggregateId: string
  expectedVersion: number     // current known version of the aggregate
  speculativeVersion: number  // expectedVersion + 1
  type: CommandType
  payload: object
  timestamp: ISO8601
  synced: boolean             // false = not yet confirmed by server
}
```

### Event types

```
// Todo
TodoCreated, TodoCompleted, TodoDeleted, TodoRenamed
TodoCategoryAssigned, TodoCategoryUnassigned
TodoDueDateSet, TodoDueDateCleared
TodoNotesUpdated, TodoProgressUpdated

// CategoryList (aggregate ID = userId)
CategoryAdded, CategoryRenamed, CategoryColorChanged
CategoryIconChanged, CategoryReordered, CategoryRemoved
```

### Command types

```
// Todo
CreateTodo, CompleteTodo, DeleteTodo, RenameTodo
AssignCategory, UnassignCategory
SetDueDate, ClearDueDate
UpdateNotes, UpdateProgress

// CategoryList
AddCategory, RenameCategory, ChangeCategoryColor
ChangeCategoryIcon, ReorderCategory, RemoveCategory
```

---

## 3. ClientEventLog

`localStorage`-backed append-only log. Keyed by `aggregateId` for efficient per-aggregate replay.

```typescript
interface ClientEventLog {
  append(event: Event): void
  getEventsFor(aggregateId: string): Event[]
  getAllEvents(): Event[]                        // ordered by (aggregateId, aggregateVersion)
  replaceSpeculative(aggregateId: string, serverEvents: Event[]): void
  getUnconfirmedCommands(): Command[]
  markSynced(commandId: string): void
}
```

On append, immediately triggers a read model rebuild for the affected aggregate.

---

## 4. Read Model Projectors

Two projectors replay events to build local read models. Rebuilt from scratch after every merge.

### LocalTodoStore

Projects `ClientEventLog` into `TodoSummary[]`. Applied in `aggregateVersion` order per todo.

```typescript
interface LocalTodoStore {
  todos: TodoSummary[]
  getById(id: string): TodoSummary | undefined
}
```

### LocalCategoryStore

Projects `ClientEventLog` into `CategorySummary[]`.

```typescript
interface LocalCategoryStore {
  categories: CategorySummary[]
  getById(id: string): CategorySummary | undefined
}
```

UI components bind to these stores only. No component ever calls the API for reads.

---

## 5. Command Dispatch Flow

```
User action
  ↓
CommandDispatcher.dispatch(command)
  ↓
1. Write command to ClientEventLog (synced: false)
2. Write speculative event (confirmed: false, source: "client")
3. Rebuild affected read model
4. Notify UI (reactive store update)
  ↓
  [if online]                      [if offline]
  ↓                                ↓
POST to API endpoint               Command stays in log (synced: false)
  with X-Expected-Version header   UI shows unsynced dot on item
  ↓                                (no spinner)
202 Accepted + Location header
  ↓
Redirect to GET /operations/{id}
  (poll until status != pending/processing)
  ↓
200 { status: "complete", event: Event }
  ↓
ClientEventLog.replaceSpeculative(aggregateId, [confirmedEvent])
  ↓
Rebuild read model → remove unsynced dot
```

### On reconnect

`ConnectivityService` fires `OnConnectivityChanged(true)`. `SyncService` picks up all unsynced commands from `ClientEventLog` and dispatches them in `timestamp` order.

---

## 6. Conflict Resolution

A conflict occurs when the server rejects a command because `expectedVersion` does not match the server's current version for that aggregate.

```
Server response: 409 Conflict
  body: { commandId, aggregateId, serverEvents: Event[] }
  ↓
ClientEventLog.replaceSpeculative(aggregateId, serverEvents)
  ↓
Rebuild read model from merged log (server events win)
  ↓
Show MudSnackbar toast:
  "[Action] on '[title]' was overridden by a newer change."
  [Redo] action button → re-queues original command with updated expectedVersion
```

Server events are inserted at their correct `aggregateVersion` positions. The client's speculative event for the same aggregate is removed. The merged log is then replayed to rebuild the read model.

---

## 7. ConnectivityService

```csharp
public interface IConnectivityService
{
    bool IsOnline { get; }
    event Action<bool> OnConnectivityChanged;
}
```

Implemented via JS interop: listens to `window` `online` / `offline` events and polls `navigator.onLine`.

Used by:
- `CommandDispatcher` — to decide immediate dispatch vs. queue
- `TaskRow` — to decide spinner vs. unsynced dot for pending commands
- `SyncService` — triggered on `OnConnectivityChanged(true)`

---

## 8. Sync Failure Handling

| Failure type | Behaviour |
|---|---|
| Transient (5xx, network error) | Retry up to 3× with exponential backoff (200ms, 400ms, 800ms) |
| Conflict (409) | Apply conflict resolution flow (section 6) |
| Validation (422) | Mark speculative event as `conflicted`; show toast: *"'[title]' couldn't be saved — [message]. [Review]"*; Review navigates to item/form with server validation errors shown inline; Cancel discards speculative event and rebuilds read model |
| Not found (404) on delete/complete | Treat as success — aggregate already gone; discard command, remove speculative event, rebuild read model |
| Auth (401) | Redirect to `/login` |
| Other 4xx | Discard command, remove speculative event, rebuild read model, show error snackbar |
| All retries exhausted | Persistent `MudAlert` (error severity): "Some changes couldn't sync. [Retry]" |

### Conflicted state

A speculative event marked `conflicted` (from a 422 response):
- Shows a warning icon on the affected item (distinct from the unsynced dot)
- Persists until the user taps Review and either corrects + resubmits or cancels
- Resubmit dispatches a new command with the corrected payload and current `expectedVersion`

---

## 9. PWA Service Worker

Blazor PWA template generates `service-worker.published.js`. Caching strategy:

| Asset type | Strategy |
|---|---|
| App shell (WASM, JS, CSS, fonts) | Cache-first, versioned. Updated on deploy via Blazor's asset manifest. |
| API calls (`/api/*`) | Network-only. Read models are local; the app never needs API reads to render. |
| Auth endpoints (`/auth/*`) | Network-only. Never cached. |

No stale API data is ever served from cache — the client read models are the cache.

---

## 10. PWA Manifest

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
