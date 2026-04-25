# End-to-end code review — TodoList (round 2)

**Date:** 2026-04-19
**Reviewer:** Claude Opus 4.7
**Branch:** `todo-list-app-278126191092115797`
**Base commit:** `ed354fb` — "fix: address Opus 4.7 review — all 6 blockers + 10 should-fix items"
**Scope:** Independent verification of the round-1 fixes plus fresh review of the follow-up commit.

---

## 1. Summary

- **Build green, 64/64 tests pass** — verified. The per-item status table in §9 of the 2026-04-17 review is substantively accurate for most items, but three claims don't hold up on inspection.
- **B6 (SignalR push) is only half-fixed.** The wire payload the server pushes (`{ type, payload }`) doesn't match what the client deserialises into (`ClientEvent` with `aggregateId`, `aggregateVersion`, `id`, `timestamp`). Field names are case-insensitive-OK for `type`/`payload`, but the other five fields are never populated, so server-pushed events land in the client store with `AggregateId = ""` and `AggregateVersion = 0`. Speculative events are never replaced and list-level `Version` tracking on the client breaks. Not caught by the test suite because no test exercises the full SignalR round-trip.
- **S8 (CommandDispatcher control flow) is not actually refactored.** §9 claims "refactored into a helper returning a discriminated result; retry loop is now straightforward." The body at `TodoList.Web/Client/Store/CommandDispatcher.cs:100-223` is still a `for` retry loop with a `try/catch` wrapping a `switch` — same structure as before, same indent confusion (`} // end for` at line 222), just slightly different wording inside. The old `ConflictResponse` type is removed (good), but the structural refactor didn't happen.
- **S9 (Web.Server proxies the API) is not fixed in code.** §9 claims "Web.Server is the BFF for the browser; Client talks to Web.Server; Aspire resolves the Api reference. Documented." The Web.Server project has no proxy/forwarder middleware. `TodoList.Web/Server/Program.cs` maps controllers + `MapFallbackToFile("index.html")`. When the WASM client calls `/todos` or `/categories` against its own origin (Web.Server), those routes will fall through to `index.html`. The AppHost's `WithReference(api)` exposes Api's URL via env vars — it doesn't install a proxy. The setup compiles and passes tests because integration tests run against the Api host directly. A real browser session would not work end-to-end.
- **Spec §1 still contradicts implementation.** §9 claims "Spec §1 updated" but §1 of `2026-04-15-review-fixes-design.md` still describes a "409 Conflict + serverEvents" response shape. The implementation is fire-and-forget 202 + async `"failed"` operation. §4 was updated correctly for the saga decision; §1 was missed.

Good news:
- B1 (snapshot), B2 (Location header), B4 (CategoryList Version++), B5 (CategoryListSummary + ListVersion wiring on the client), S1/S2 (seed endpoint gone, auto-seed on GET/mutation), S3 (Domain is WolverineFx-free, `[SagaInitiator]` discovery works), S4 (OperationResponse shape matches), S6 (cross-user isolation tests present and passing), S10 (dual `/api/me` with aligned shapes and documented ownership) are all correctly implemented.
- The security fix — `.RequireAuthorization()` on `/todos`, `/categories`, `/todos/operations/{id}` — is in place and verified. `AuthEndpoints.cs` and `EventHub` are also authenticated. No endpoint I could find is missing auth.

---

## 2. Verification against §9 status table

| ID | Claimed | Verified? | Notes |
|---|---|---|---|
| B1 Snapshot regen | ✅ Fixed | **Confirmed** | `TodoList.Api/Migrations/TodoDbContextModelSnapshot.cs` now includes `UserId` + `Version` on `Todo`, `TodoSummary`, `CategorySummary`, and `CategoryListEntity`. Designer file for the migration also present. Integration suite boots cleanly. |
| B2 Poller URL | ✅ Fixed | **Partial** | `CommandDispatcher.cs:130-134` now extracts the operation ID from the 202 `Location` header. `OperationPoller.cs:29` still has a hard-coded `/todos/operations/{operationId}` path — it receives the parsed ID, not the full location. That's fine in practice because server and client agree on the path, but the claim "no hard-coded path" is overstated. The right fix is to pass the full Location URL to the poller and let it GET directly. |
| B3 Concurrency Option A | ✅ Fixed | **Confirmed** | Server is 202 + async fail on conflict. `ConflictResponse` type removed. Client has a `Contains("Version conflict")` branch at `CommandDispatcher.cs:144` that discards speculative and snackbar-warns. Note: this couples the client to a server error-message substring — see Should-fix S11 below. |
| B4 `CategoryList.AddCategory` Version++ | ✅ Fixed | **Confirmed** | `TodoList.Domain/Aggregates/CategoryList.cs:62` has `Version++`. Unit test `AddCategory_increments_version` present in `CategoryListTests.cs:62-69`. |
| B5 `Categories.razor` list-level Version | ✅ Fixed | **Confirmed at source** | `Categories.razor` uses `CategoryStore.ListVersion` at lines 75, 84, 111, 120, 136, 146. `ILocalCategoryStore.ListVersion` backed by `CategoryListSummary.Version`, populated by `CategoryProjector.ProjectList` via `events.Max(e => e.AggregateVersion)`. **But see S12 below** — `StartupSeedService.SeedCategoriesAsync` seeds each category as a `CategorySeeded` event with `AggregateVersion = 0`, so `ListVersion` starts at 0 on fresh load. That works because server handlers skip the version check when `ExpectedVersion == 0`, but it means optimistic concurrency is effectively bypassed for the first mutation after each fresh load. |
| B6 SignalR push | ✅ Fixed | **NOT verified** — wire shape mismatch. See §3. |
| S1 Remove `/categories/seed` | ✅ Fixed | **Confirmed** | No `MapPost("/seed")` anywhere. Auto-seed inline in `CategoryEndpoints.cs:22-46`. Tests no longer call a seed endpoint. |
| S2 `AddCategory` auto-create list | ✅ Fixed | **Confirmed** | `CategoryCommandHandlers.cs:20-28` creates list + cascades seed events on first-use. |
| S3 Saga back in Api | ✅ Fixed | **Confirmed** | `DueReminderSaga` + `DueReminderMessage` under `TodoList.Api/Sagas/`. `TodoList.Domain/TodoList.Domain.csproj` has zero package references. `[SagaInitiator]` on `TodoDueDateSetEvent`. Client discovery at `CommandDispatcher.cs:50-63` reflects over `IDomainEvent.Assembly`. Works. |
| S4 `OperationResponse` shape | ✅ Fixed | **Confirmed** | Client record at `OperationPoller.cs:65-71` is `{ Status, Result (JsonElement?), FailureReason, IsRetryable }`. Matches server's `OperationEndpoints.cs:22-31`. |
| S5 `SeedCategoriesCommand` gone | ✅ Fixed | **Confirmed** | `CategoryListCommands.cs` has only the 6 mutation commands. |
| S6 Cross-user isolation | ✅ Fixed | **Confirmed** | `CrossUserIsolationTests.cs` has two tests, both passing. `TestAuthHandler.UserHeader = "X-Test-User"` flip works. |
| S7 Auto-migrate in Testing | ✅ Acknowledged | **Confirmed** — left as-is, called out. |
| S8 Dispatcher control flow | ✅ Fixed | **NOT verified** — same structure as before. See §3. |
| S9 Web.Server proxies Api | ✅ Fixed | **NOT verified** — no proxy wired. See §3. |
| S10 Two `/api/me` | ✅ Fixed | **Confirmed** | Shapes aligned to `{ UserId, Email, Name, AuthMethod }` in both `AuthController.cs:51-57` and `AuthEndpoints.cs:29-35`. Both docstrings explain ownership. Client `UserProfileStrip` expects the same shape. |

Critical security fix (`RequireAuthorization()` on the endpoint groups) is in place:
- `TodoEndpoints.cs:15` — `app.MapGroup("/todos").RequireAuthorization()` ✅
- `CategoryEndpoints.cs:15` — `app.MapGroup("/categories").RequireAuthorization()` ✅
- `OperationEndpoints.cs:32` — `.RequireAuthorization()` on the single route ✅
- `AuthEndpoints.cs:36, 43` — both `/api/me` and `/api/auth/logout` have it ✅
- `EventHub.cs:8` — `[Authorize]` on the hub class ✅

Spot-check: no other `app.Map*` routes that would bypass auth. The only unauthenticated paths are `/health/live`, `/health/ready`, and `MapOpenApi()` in dev — all intentional.

---

## 3. Blockers

### B7. SignalR push payload shape doesn't match client event shape

- `TodoList.Api/EventHandlers/TodoProjectionHandler.cs:117-121` pushes:
  ```csharp
  await hub.Clients.Group($"user:{userId}").ReceiveEvent(new
  {
      type = evt.GetType().Name.Replace("Event", ""),
      payload = evt
  });
  ```
- `TodoList.Api/EventHandlers/CategoryProjectionHandler.cs:91-96` pushes the same shape.
- Client subscribes at `TodoList.Web/Client/Hubs/EventHubClient.cs:26-30`:
  ```csharp
  _connection.On<ClientEvent>("ReceiveEvent", evt =>
  {
      _store.AppendEvent(evt with { Source = EventSource.Server, State = EventState.Confirmed });
  });
  ```
- `ClientEvent` requires `{ Id, AggregateId, AggregateVersion, Type, Payload, Timestamp, Source, State }`.

System.Text.Json will bind `type` → `Type` and `payload` → `Payload` (case-insensitive). `AggregateId`, `AggregateVersion`, `Id`, and `Timestamp` have no source fields, so they default to `""`, `0`, a new Guid, and `DateTimeOffset.MinValue` (actually — `Timestamp` has `DateTimeOffset.UtcNow` as initializer, but only if the constructor runs; in System.Text.Json record deserialisation with a missing property, it uses the default for `DateTimeOffset`, which is `DateTimeOffset.MinValue`).

Downstream effects:

1. `ClientStore.AppendEvent` calls `OnAggregateChanged(evt.AggregateId)` with `""` — no listener targets the real aggregate, so `LocalTodoStore` / `LocalCategoryStore` never rebuild for the actual aggregate.
2. `CategoryProjector.ProjectList` computes `Version = events.Max(e => e.AggregateVersion)` — server-pushed events contribute 0, so `ListVersion` never advances from server pushes.
3. `CommandDispatcher.cs:138-142` comments: "The authoritative event will arrive via SignalR push; mark the command synced so UI stops showing 'unsynced' state. The speculative event remains in place until the server event replaces it via ReplaceSpeculative triggered by the push." But nothing calls `ReplaceSpeculative` on server push — `EventHubClient` just `AppendEvent`s, and `AggregateId = ""` doesn't match any speculative event. Speculative events are orphaned; the user's state diverges from the server's over time.

Fix: the server should push a properly-shaped `ClientEvent`:

```csharp
await hub.Clients.Group($"user:{userId}").ReceiveEvent(new ClientEvent
{
    Id = Guid.NewGuid().ToString(),
    AggregateId = GetAggregateId(evt),
    AggregateVersion = GetAggregateVersion(evt), // from projection or event
    Type = evt.GetType().Name.Replace("Event", ""),
    Payload = JsonSerializer.SerializeToElement(evt),
    Timestamp = DateTimeOffset.UtcNow,
    Source = EventSource.Server,
    State = EventState.Confirmed
});
```

Plus: `EventHubClient` should call `ReplaceSpeculative` (or equivalent) when the pushed event's `AggregateId` matches a speculative one, not just `AppendEvent`. Today `ReplaceSpeculative` is only called from nowhere — the code path was designed but never hooked up.

This is a functional blocker for the realtime story. The integration tests don't catch it because they exercise server-only flows via HTTP.

### B8. `LocalTodoStore` was not audited

I checked `LocalCategoryStore` carefully and the pattern relies on projecting from `GetAllEvents()`. Look at `LocalTodoStore.cs` too — the Todos page reads `todo.Version` everywhere. If `LocalTodoStore`'s projection doesn't compute `Version` properly from events (same issue as B5 originally was for categories), every TodoDetail mutation after the first is going to conflict. Not verified in this pass but worth a spot-check.

---

## 4. Should-fix

### S11. Version-conflict detection relies on a substring match

`CommandDispatcher.cs:144`:
```csharp
else if (result.FailureReason?.Contains("Version conflict", StringComparison.OrdinalIgnoreCase) == true)
```

Server writes the failure reason as `$"Version conflict: expected version does not match server version {serverVersion}"`. Change the string and every client breaks silently. Fix: server returns a typed error code (e.g. `FailureCode = "VERSION_CONFLICT"`) on the operation row, client branches on that.

Same concern called out as N-class in 2026-04-17; upgrading because it's load-bearing for UX now that it's the only code path that distinguishes conflict from "real" failure.

### S12. First-load client `ListVersion` is 0, bypassing optimistic concurrency

`StartupSeedService.SeedCategoriesAsync` seeds each category summary as a `CategorySeeded` event with `AggregateVersion = 0`. `CategoryProjector.ProjectList` returns `Version = 0`. First category mutation sends `ExpectedVersion = 0`, which the server skips (`ExpectedVersion > 0 && ...` is false). So optimistic concurrency is bypassed on that first command.

This works (tests pass, no 409s) but defeats the point of the fix. Options:

- Server returns the `CategoryList.Version` in `GET /categories` response body (e.g. wrap the list as `{ version, categories: [...] }`) and the client seeds it into `ListVersion` separately.
- Or `SeedCategoriesAsync` inspects the response and populates each event's `AggregateVersion` from a `version` field.

Same issue applies to todos — each `TodoSeeded` event is stamped with `AggregateVersion = 0` regardless of the summary's `Version` column. The Todos page seeds via `todo.Version` from the summary, so it's fine there, but the state-over-time projection of `AggregateVersion` is misleading.

### S13. `CategoryEndpoints.cs` GET auto-seed writes directly to the read model

`CategoryEndpoints.cs:28-45` — when the list is null, the endpoint creates the aggregate and hand-inserts `CategorySummaryEntity` rows directly, bypassing Wolverine. The spec is explicit that projections are updated by event handlers. Pragmatic (avoids a race between the response and async projection), but inconsistent. Two concerns:

1. Consistency: the command-driven path goes through projection handlers; this path doesn't. A future change to `CategoryProjectionHandler.Handle(CategoryAddedEvent)` (e.g. add a new field) will need to be duplicated here.
2. Concurrency: if two requests hit `GET /categories` simultaneously for a brand-new user, both see `list is null`, both insert the same four category entities. SQL unique constraints on `(UserId, Name)` would catch it — there aren't any today. Small window, small blast radius (dupe categories in the projection), but worth a `try/catch UniqueConstraintViolation` or a `SemaphoreSlim` per user.

Alternative: fire a `SeedCategoryListCommand` through Wolverine from the GET handler, then re-query. Slightly slower but consistent.

### S14. S8 was claimed fixed but the control flow is unchanged

Re-reading `CommandDispatcher.cs:100-223`:
- `for` retry loop at line 105
- `try/catch` inside at lines 107/213
- `switch (response.StatusCode)` inside the try at line 126
- Each `case` has its own `break` to exit the switch
- Line 211 has a `break;` comment as "exit the retry loop"
- Line 222 has `} // end for`

The indentation is still split — the switch is indented 4 spaces deeper than the try, and `}` closing the switch is under the case bodies, not under `switch`. `// end for` comment on line 222 is a tell-tale sign the reader needs it.

The claim "refactored into a helper returning a discriminated result; retry loop is now straightforward" doesn't match. What changed from the earlier version: the `ConflictResponse` handling was deleted (part of B3). That's real but unrelated to the flow refactor. Fix: extract `HandleResponse(response)` returning an enum `{ Retry, Done, Discard }`, then the retry loop reads:

```csharp
for (var attempt = 0; attempt <= maxRetries; attempt++)
{
    try
    {
        var response = await _http.SendAsync(request);
        var outcome = await HandleResponse(response, command, aggregateId);
        if (outcome == Outcome.Retry && attempt < maxRetries)
        {
            await Task.Delay(retryDelaysMs[attempt]);
            continue;
        }
        return;
    }
    catch (HttpRequestException) when (attempt < maxRetries)
    {
        await Task.Delay(retryDelaysMs[attempt]);
    }
}
```

### S15. S9 is documentation-only, not code

`TodoList.Web/Server/Program.cs` — no YARP, no `MapForwarder`, nothing that proxies `/todos`, `/categories`, `/hubs/events` from Web.Server to Api. The client's `HttpClient.BaseAddress` is `builder.HostEnvironment.BaseAddress` = the Web.Server host origin. Calls to `/todos` against that origin fall through `MapFallbackToFile("index.html")` and return the HTML shell — `GetFromJsonAsync<JsonElement[]>` will throw.

Two options:

1. Add YARP to Web.Server and `MapForwarder("/todos/{**catch-all}", api)`, `/categories/**`, `/hubs/events/**` to the Api service.
2. Or serve Blazor assets from the Api host directly (one process) and drop Web.Server. That removes the BFF pattern but simplifies operationally.

The spec chose option 1 implicitly (Web.Server as BFF). Ship the YARP wiring. Integration tests won't fail without it because they hit the Api directly.

### S16. Spec §1 still says 409 + serverEvents

`docs/superpowers/specs/2026-04-15-review-fixes-design.md:13-24` describes a 409 response shape with `serverEvents`. The implementation is 202 + async fail. §9 of the 2026-04-17 review claims "Spec §1 updated" but the update didn't land in §1 — only §4 was edited.

Fix: rewrite §1 to describe the fire-and-forget + async `"failed"` with `"Version conflict"` flow. Also update §1 verification ("two concurrent mutations → 409") to match `ConcurrencyTests.cs` (→ operation `"failed"`).

---

## 5. Nits

### N14. `TodoList.Web/Client/Services/OperationPoller.cs:29` hard-codes the `/todos/operations/{id}` path
Better: accept the full Location URL or a path from the caller. `B2` called this out with "Fix: make the Location header the source of truth" — the CommandDispatcher does extract from Location, but then it discards everything except the last path segment and the poller rebuilds the URL. Pass the URL through.

### N15. `EventHubClient._hubUrl` string interpolation still relies on trailing slash
`$"{http.BaseAddress}hubs/events"` — carried over from N11 in the previous review. Safe today because the WASM `HostEnvironment.BaseAddress` always ends in `/`, but `new Uri(http.BaseAddress, "hubs/events").ToString()` is the idiomatic fix. Tiny, left over.

### N16. `NotificationHandlers.Handle` is still log-only
Flagged as N9 last time. Fine, but a `// TODO(plan-?)` marker would help — spec says this eventually goes to email.

### N17. `TestAuthHandler` impersonation header is unauthenticated for production use
Only bound in `Testing` environment via `ApiFixture` — safe. Worth a comment in the handler itself noting this is test-only so a future developer doesn't copy-paste it.

### N18. Health check not bound to `/api/me`
`/api/me` requires auth. A health probe can't use it. Fine — `/health/live` and `/health/ready` exist. Just noting the split.

### N19. `CategoryProjectionHandler.Handle(CategoryAddedEvent)` writes `Version = 1` — new vs. existing summary
If an event is replayed (it shouldn't, but durability can do surprising things), this overwrites any accumulated Version. Prefer `Version = 1` only on insert; on conflict increment. Low priority — Wolverine's outbox should prevent replay.

---

## 6. Per-spec conformance (2026-04-15 review fixes, round 2)

| Spec section | Status after round-2 fixes |
|---|---|
| §1 409 + serverEvents | **Spec out of date.** Implementation is 202 + async "failed". Update the spec. |
| §1 `Todo` + `CategoryList` carry Version | ✅ |
| §1 Client `ExpectedVersion` from read model | ✅ for Todos, ✅ for Categories (via `ListVersion`). But initial load is always 0 — see S12. |
| §1 Verification: two concurrent mutations → 409 | Test exists (`ConcurrencyTests.Concurrent_mutations_with_stale_version_fails_operation`) and asserts `failed` not `409`. Matches implementation. Spec language needs updating. |
| §2 URL fixes | ✅ |
| §2 POST /todos with optional fields | ✅ |
| §3 Wolverine dispatch | ✅ `bus.InvokeAsync` used consistently. Handlers return cascade arrays. Projection handlers update read models and push SignalR (with caveat B7). |
| §3 DueReminderSaga fires naturally | ✅ via `[SagaInitiator]` attribute. Unit tests cover state transitions. No end-to-end test with a deterministic clock. |
| §4 Saga in Api, attribute on event | ✅ Domain is WolverineFx-free. Discovery works. |
| §5 RemoveCategory cascade | ✅ `CategoryProjectionHandler.Handle(CategoryRemovedEvent)` unassigns todos at line 85-87. `CategoryRemovedCascadeHandler` not needed because the projection handler already does the unassign. |
| §6 Todo.UserId | ✅ |
| §6 `/api/me` | ✅ (dual implementation, documented) |
| §6 IsUnsynced / IsConflicted | ✅ |
| §7 MudBlazor 8 | ✅ (unchanged from previous review) |
| §7 POST /categories/seed removed | ✅ |
| §7 TodoList/ stub deleted | ✅ (unchanged) |
| §7 3x retry on 5xx | ✅ still present at `CommandDispatcher.cs:120-124` |
| §7 Null-check dialog.Result | ✅ (unchanged) |

---

## 7. Per-commit review (ed354fb)

**ed354fb — fix: address Opus 4.7 review — all 6 blockers + 10 should-fix items**

37 files changed, +1074/-214. Broadly competent, but three overclaims in the commit message:

- `B2: OperationPoller uses the 202 Location header, no hard-coded path` — the CommandDispatcher extracts from Location but the poller itself still hard-codes `/todos/operations/{id}`. See N14.
- `B6: projection handlers push ReceiveEvent through IHubContext to users` — code path exists, but the pushed payload shape doesn't match what the client deserialises into. See B7.
- `S8: Refactor CommandDispatcher switch out of the retry loop` — the switch is still inside the retry loop. See S14.

What the commit did do well:
- Regenerated the DbContext snapshot correctly (designer + snapshot both present).
- Added `CategoryListSummary` and wired `LocalCategoryStore.ListVersion` end-to-end through the Categories page.
- Added `[SagaInitiator]` attribute cleanly — discovery is one loop over one assembly, no reflection traps.
- Cross-user isolation test + `X-Test-User` header impersonation is a nice, minimally invasive pattern.
- The `.RequireAuthorization()` security fix is real and the tests catch a regression if removed.
- `/api/me` shape alignment is tight.
- `CategoryList.AddCategory` unit test for `Version++` — exactly the missing coverage.

---

## 8. Build and test results

### Build
- `dotnet build TodoList.sln` — **success, 14 warnings, 0 errors.**
- Warnings unchanged from last review: `NU1608` Microsoft.CodeAnalysis version mismatch across MCP projects. Not blocking.

### Tests
- `dotnet test TodoList.sln --no-build` — **64/64 pass.**
- Unit tests: 37/37 (`TodoList.Tests.dll`, 29 ms).
- Integration tests: 27/27 (`TodoList.IntegrationTests.dll`, 31 s).
- Test suite genuinely green — no skipped, no quarantined.

### End-to-end (Aspire)
- Not exercised. Given S15 (no proxy), a real browser session hitting the Web.Server origin will not be able to reach `/todos`, `/categories`, or `/hubs/events`. The integration tests don't catch this because they create a WebApplicationFactory against the Api project directly.

---

## 9. Reviewer's take

The round-2 fixes landed most of the important work. Domain separation is clean, auth scoping is airtight, optimistic concurrency on the server is correct, the saga lives where it belongs, and the cross-user test surfaced a real security hole that got fixed cleanly. That's genuinely useful.

The three misses share a theme: **claims that outpaced the code.** The SignalR push was added but the wire shape wasn't verified. The CommandDispatcher was touched but not meaningfully refactored. The BFF proxy is documented but not implemented. Each item is individually fixable in a commit or two.

**Priority order:**
1. B7 — SignalR payload shape. Without this, the whole realtime story silently breaks the client state. Add a test that connects a SignalR client to the test host, posts a command, asserts the pushed event has the expected shape.
2. S15 — Wire up YARP or similar in Web.Server so the browser can actually reach the Api through its origin. Without this, the only way to run the app is to point the browser directly at the Api port.
3. S16 — Update spec §1 to match the async "failed" flow. Small but keeps the docs honest.
4. S11/S12 — the "version conflict" substring match and the first-load ListVersion=0 issue. Low urgency, but they'll bite when the first real concurrent user hits.
5. S14 — the dispatcher refactor. Low urgency; correctness is fine, readability is not.

Everything else is in reasonable shape. The 64/64 test count is genuinely comforting — all the round-1 regressions are pinned.

---

## 10. What I checked vs. what I didn't

**Checked:**
- Every file in the ed354fb commit, with particular attention to the six claimed blocker fixes.
- `CommandDispatcher.cs` control flow (read every line).
- SignalR push/receive wire shape (server push sites + client `On<ClientEvent>` handler).
- `RequireAuthorization()` on all `MapGroup` and `Map*` routes in the Api.
- Spec §1 and §4, plan §11.
- Full build + full test suite (unsandboxed).
- Cross-user isolation test mechanics.

**Not checked (worth a look later):**
- `LocalTodoStore` projection logic (see B8).
- Whether `LocalCategoryStore.Rebuild()` is re-entrant safe if multiple events append in quick succession.
- MCP servers (`Mcp.Tools`, `Mcp.Composite`) — compile clean but not exercised against a real MCP client.
- PWA offline behaviour — no tests.
- The Aspire AppHost end-to-end smoke.
