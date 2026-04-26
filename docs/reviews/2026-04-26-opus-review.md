# Round-3 Code Review (Opus 4.7)

**Date:** 2026-04-26
**Reviewer:** Opus 4.7 via superpowers:code-reviewer agent
**HEAD reviewed:** `29ed4a7` — `fix: round 2 — async concurrency via operation poll + GET /categories envelope`
**Spec:** `docs/superpowers/specs/2026-04-15-review-fixes-design.md`

Tests: 64/64 green at HEAD. Most failure modes below are masked by the test suite passing only because of B1.

---

## Blockers

### B1. `bus.InvokeAsync` instead of `bus.PublishAsync` — direct spec violation

**Files:** `TodoList.Api/Endpoints/TodoEndpoints.cs:51,64,71,78,85,92,99,106,113,120,127`, `TodoList.Api/Endpoints/CategoryEndpoints.cs:77,84,91,98,105,112`

Spec §3 reads: *"Every mutating endpoint calls `IMessageBus.PublishAsync(command)` and returns 202+Location immediately — the endpoint does not wait for the command to be handled."* Every endpoint uses `bus.InvokeAsync`, which is Wolverine's request/reply form — it dispatches the command in-process and **awaits the handler** before returning 202.

Practical effects:

1. The 202 is a lie — by the time the client gets it, the operation is already terminal. The client poll on the first attempt will see "complete" or "failed", never "processing". The whole "processing then poll until terminal" model the spec describes never actually exercises its async path.
2. Endpoint latency is now bounded by handler latency (and any cascading work that happens to run inline).
3. With Wolverine SQL Server durability enabled in non-test environments, `InvokeAsync` semantics differ from `PublishAsync` — durability/outbox guarantees you'd expect to be in play (spec says "fire-and-forget") aren't.

**Fix:** Replace every `await bus.InvokeAsync(...)` with `await bus.PublishAsync(...)`. Existing integration tests will keep passing because they poll, but real conflicts/processing become observable.

### B2. `DueReminderSaga` will never trigger — saga flow broken

**Files:** `TodoList.Api/Sagas/DueReminderSaga.cs:31`, `TodoList.Api/Handlers/TodoCommandHandlers.cs:191,252`

The handlers wrap every domain event in `UserScopedEvent(userId, aggregateId, version, IDomainEvent inner)` and return that as the cascading message. Wolverine routes by message type — so it dispatches `UserScopedEvent` to the projection handlers (which destructure it), but **never** to `DueReminderSaga.Start(TodoDueDateSetEvent)`. The bare `TodoDueDateSetEvent` is never published to the bus.

Spec §3 says: *"The existing DueReminderSaga fires naturally because domain events flow through Wolverine after the command handler publishes them."* It does not — only the wrapper flows. No integration tests for the saga, so this regression isn't caught.

**Fix options:**
- Re-publish the inner `IDomainEvent` from the projection handler (or a dedicated unwrapping handler) so the saga can receive it.
- Or: change the saga signature to take `UserScopedEvent` and check the inner type.
- Or: drop the wrapper and pass `userId`/`aggregateVersion` via Wolverine envelope/headers (`MessageContext`) instead. Lean towards this — the wrapper is the root cause of S7 too.

### B3. CategoryList persistence is one-way — Version + mutations to the aggregate are never saved

**Files:** `TodoList.Api/Data/CategoryListRepository.cs:23-43`, `TodoList.Api/Handlers/CategoryCommandHandlers.cs:38,57,75,93,111,129`

`CategoryListRepository.GetByUserIdAsync` reads the EF-tracked `CategoryListEntity` and constructs a fresh in-memory `CategoryList` aggregate via `Reconstitute`. The aggregate has no link back to the tracked entity. When the handler calls `list.RenameCategory()`/`AddCategory()`/etc and increments `list.Version`, **those mutations never reach the database**. There's no `UpdateAsync` and the entity's tracked state is unchanged.

`SaveAsync()` is a no-op for any `Rename/ChangeColor/ChangeIcon/Reorder/Remove` command — the only thing that ever lands in `CategoryListEntity` is the initial `AddAsync` snapshot from auto-seed (with `Version = 0`). So:

- `CategoryListEntity.Version` is permanently 0 in the DB.
- Subsequent `list.Version != cmd.ExpectedVersion` checks always see `list.Version = 0`:
  - If client sends `ExpectedVersion = 0` (e.g. after first GET), check is skipped (`> 0` guard).
  - If client sends `ExpectedVersion > 0` (after several mutations), check fails — `0 != N` — every mutation conflicts.
- `Categories` collection in `CategoryListEntity` is also stale — only `CategorySummaryEntity` reflects renames.

Optimistic concurrency on `CategoryList` is **non-functional in both directions**. No tests for `CategoryList` concurrency to catch this.

**Fix:** Either treat `CategoryList`/`Category` like `Todo` (aggregate IS the EF entity, EF tracks the mutations) or add a real `Update` path on the repo that syncs the in-memory aggregate state back onto the tracked entity (Version + Categories collection diff).

### B4. `OperationPoller.PollAsync` immediately fails on 200 + status="processing"

**File:** `TodoList.Web/Client/Services/OperationPoller.cs:25-60`

```csharp
if (response.IsSuccessStatusCode) {
    var terminal = body.Status switch { ... "pending" or "processing" => null, ... };
    if (terminal is not null) return terminal;
    // falls through with terminal == null
}
if ((int)response.StatusCode >= 500) { /* retry */ }
else { return OperationResult.Failed($"HTTP {(int)response.StatusCode}"); }
```

When a 200 lands with `status = "processing"`, `terminal` is `null`, the inner `return` is skipped, then we fall through to the second block. Status is 200 (< 500) so we hit the `else` and return `OperationResult.Failed("HTTP 200")`. The poll loop never gets a chance to delay-and-retry on a genuinely processing op.

Today this is masked because B1's `InvokeAsync` means the operation is always terminal by the time the client polls. As soon as B1 is fixed, every async mutation will appear to fail immediately.

**Fix:** Make the success-with-non-terminal branch fall through to the delay-and-retry tail explicitly:

```csharp
if (response.IsSuccessStatusCode) {
    var body = ...;
    if (body is null) return OperationResult.Failed("Empty response");
    var terminal = body.Status switch { ... };
    if (terminal is not null) return terminal;
    // pending/processing — fall through to delay
}
else if ((int)response.StatusCode >= 500) { /* retry */ }
else { return OperationResult.Failed($"HTTP {(int)response.StatusCode}"); }

if (attempt >= delays.Length) return OperationResult.Failed("Polling timeout");
await Task.Delay(delays[attempt++], ct);
```

### B5. `LocalCategoryStore.ListVersion` is computed across all events, not just the CategoryList aggregate

**Files:** `TodoList.Domain/Projectors/CategoryProjector.cs:21`, `TodoList.Web/Client/Store/LocalCategoryStore.cs:26-30`

`CategoryProjector.ProjectList` does:

```csharp
var version = events.Count > 0 ? events.Max(e => e.AggregateVersion) : 0;
```

`LocalCategoryStore.Rebuild()` passes `_clientStore.GetAllEvents()` — every event in the store, including all Todo events. After a few todo mutations the max aggregate version across the whole store will dwarf the actual `CategoryList` version.

`Categories.razor` then reads `CategoryStore.ListVersion` and stamps it onto `ClientCommand.ExpectedVersion` for every category mutation. The server (when B3 is fixed) will reject every category mutation as a version conflict because the client's "expected" is far ahead of the actual list version.

**Fix:** Filter events by `AggregateId == "user-category-list"` before taking the max:

```csharp
var version = events
    .Where(e => e.AggregateId == "user-category-list")
    .Select(e => (int?)e.AggregateVersion)
    .Max() ?? 0;
```

---

## Should fix

### S1. `TodoOperation` has no `UserId` — cross-user info leak via operation poll

**Files:** `TodoList.Api/Operations/TodoOperation.cs:1-30`, `TodoList.Api/Endpoints/OperationEndpoints.cs:10-33`

`GET /todos/operations/{id}` requires authentication but does not check the operation belongs to the caller. Anyone authenticated who guesses (or observes through a side channel) another user's operation GUID can read its result/failure. Ops carry the result of mutations including category names, todo titles, etc.

**Fix:** Add `UserId` (string) to `TodoOperation`, set on creation in the endpoints, and `404` from the GET endpoint when `operation.UserId != callerUserId`.

### S2. `CategoryEndpoints.GET` auto-seed bypasses the event/projection pipeline

**File:** `TodoList.Api/Endpoints/CategoryEndpoints.cs:30-58`

The GET endpoint creates the `CategoryList` aggregate AND directly inserts `CategorySummaryEntity` rows for the seeded categories. The `CategoryAddedEvent`s from `CategoryList.Create` are discarded — not published, no SignalR push.

A connected SignalR client doing `GET /categories` will see the seeded categories in the response, but their event store won't get the corresponding `CategoryAdded`/`CategorySeeded` events from the push pipeline. They have to rely on `StartupSeedService` reading the GET response and synthesizing `CategorySeeded` events. The handler-side auto-seed in `AddCategoryCommand.Handle` DOES push events, so behaviour diverges.

**Fix:** Pick one auto-seed path. Either drop the GET auto-seed (GET on a brand new user returns `{ version: 0, categories: [] }` until the client dispatches an explicit add), or have GET publish a `SeedCategoriesCommand` and wait/poll.

### S3. `ConcurrencyTests` still substring-matches `failureReason`

**File:** `TodoList.IntegrationTests/Api/ConcurrencyTests.cs:35`

```csharp
op2.GetProperty("failureReason").GetString().Should().Contain("Version conflict");
```

Spec §1 explicitly forbids substring matching of `FailureReason` — the typed `FailureCode` is the contract.

**Fix:** assert on `failureCode == "VERSION_CONFLICT"`.

### S4. Two `FailureCodes` constant classes exist with overlapping values

**Files:** `TodoList.Api/Handlers/TodoCommandHandlers.cs:238-244`, `TodoList.Web/Client/Services/OperationPoller.cs:88-95`

Identical constants in two places, guaranteed to drift. Spec wants "known string constants" — should live in one shared place.

**Fix:** Move `FailureCodes` to `TodoList.Domain` and delete the duplicates.

### S5. `[SagaInitiator]` toast fires but no work runs (downstream of B2)

**File:** `TodoList.Web/Client/Store/CommandDispatcher.cs:50-63`

Becomes correct automatically once B2 is fixed.

### S6. Two SaveChanges calls in `CategoryEndpoints.GET` seed are unnecessary

**File:** `TodoList.Api/Endpoints/CategoryEndpoints.cs:48-49`

`listRepo.SaveAsync()` and `db.SaveChangesAsync()` both call `db.SaveChangesAsync()` on the same DbContext. Drop one.

### S7. `CategoryRemovedCascadeHandler` runs on every `UserScopedEvent`

**File:** `TodoList.Api/Handlers/CategoryRemovedCascadeHandler.cs:17-19`

Handler signature is `Handle(UserScopedEvent envelope, ...)` and short-circuits with `if (envelope.Event is not CategoryRemovedEvent evt) return [];`. Called for every domain event in the system, takes the cost of opening a scope, then immediately returns. With durability + outbox, persisted as a "handled" message per event.

**Fix:** After B2, take the bare `CategoryRemovedEvent` directly. Wolverine then only invokes it on the right type.

### S8. `WrapEvents` uses post-mutation `aggregateVersion` for ALL events including auto-seed

**File:** `TodoList.Api/Handlers/CategoryCommandHandlers.cs:41`

In `AddCategoryCommand.Handle`, when auto-seed fires we end up with 4 seed events + 1 user-add event, all wrapped with the same `list.Version` = 1. Surprising if you read the code.

**Fix (after B3):** wrap each event with the aggregate version *as-of that event*.

---

## Nits

### N1. Dispatcher swallows non-conflict failures silently

**File:** `TodoList.Web/Client/Store/CommandDispatcher.cs:202-207`

For a `failed` op with a non-conflict `FailureCode` (`VALIDATION_ERROR`, `NOT_FOUND`, `INTERNAL_ERROR`), the client discards speculative + marks synced + shows a generic snackbar. Validation errors specifically deserve the same treatment as the synchronous 422 path (`HandleValidationConflictAsync`) — surfacing field-level errors instead of a single string.

### N2. `OperationResponse.Result` is a nullable `JsonElement?`

**File:** `TodoList.Web/Client/Services/OperationPoller.cs:69`

`JsonElement` is a struct. `JsonElement?` is `Nullable<JsonElement>`, but `System.Text.Json` deserializes a missing property as `default(JsonElement)` (`ValueKind = Undefined`) rather than `null`. Doesn't bite today.

### N3. `GetExpectedVersion` co-location

**Files:** `TodoList.Api/Endpoints/CategoryEndpoints.cs:120-121`, `TodoList.Api/Endpoints/TodoEndpoints.cs:135-136`

Worth co-locating with `CreateOperation` to make the relationship obvious.

### N4. `CommandDispatcher` reflection runs per construction

**File:** `TodoList.Web/Client/Store/CommandDispatcher.cs:36`

`DiscoverSagaInitiatingTypes()` runs in the ctor. Cheap, but easily hoisted to a `static readonly` for clarity.

---

## Suggested order of work

1. **B4** (`OperationPoller`) first — small, isolated, stops B1's fix from regressing the UI.
2. **B1** (Invoke→Publish) — flips the system into the actually-async mode the spec describes.
3. **B5** (`ListVersion` filter) — small one-liner, removes a hidden ticking time bomb.
4. **B3** (CategoryList persistence) — the structural one. Add an integration test that mutates a category twice and asserts the second mutation can use `ExpectedVersion = 1`.
5. **B2** (saga flow). Add at least one integration test that asserts `DueReminderSaga` fires.
6. **S1** (operation user scoping) before any non-test deployment.
7. The rest in any order.

After 1-5 you'll have the actual contract the spec describes. Today the test suite passes because the failure modes hide behind `InvokeAsync`'s synchronous-by-accident behaviour and the lack of CategoryList concurrency tests.
