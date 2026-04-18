# End-to-end code review — TodoList

**Date:** 2026-04-17
**Reviewer:** Claude Opus 4.7
**Branch:** `todo-list-app-278126191092115797`
**Commits covered:** e66236a, 4daba50, 6cd77cf, 61895c6, b34d73e (plus enough of the preceding Plan H commits to make sense of them)

---

## 1. Summary

- **The build compiles but the integration test suite is red.** 21 of 25 integration tests fail at fixture init. Single root cause: `TodoDbContextModelSnapshot.cs` was not regenerated after migration `20260415000000_AddUserIdVersionToTodo`, so EF throws `PendingModelChangesWarning` on `MigrateAsync()`. This is a one-command fix but nothing downstream runs until it's fixed.
- **The concurrency round-trip is broken end-to-end.** Server went async (202 + "failed" operation) in commit 18fe57c/b34d73e, but the client still treats conflicts as `409` + `serverEvents` in `CommandDispatcher.cs`. The `409` branch is dead code, and the async path never confirms speculative events because the operation endpoint returns `result` (identity) not `event` (the client's `OperationResponse.Event` is always null).
- **`OperationPoller` hits `/operations/{id}` but the server is at `/todos/operations/{id}`.** It will always 404 and the poller treats that as "HTTP 404 → failed". Every create/rename/etc. will look failed to the client even though the server succeeded. This is a separate bug from the concurrency one, but compounds it.
- **`CategoryList.AddCategory` never increments `Version`.** Every other mutation does. The CategoryList aggregate will silently accept stale writes the moment any category is added to the list.
- **`Categories.razor` passes `cat.Version` (a per-summary version) as `ExpectedVersion` against the `CategoryList` aggregate, which has a single list-level `Version`.** Even if the snapshot were fixed, every category mutation from the UI will conflict after the first real operation because the two numbers don't track the same thing.
- **SignalR push is not wired up on the server.** `EventHub` exists and is mapped at `/hubs/events`, but nothing on the server ever calls `IHubContext<EventHub>.Clients.*.ReceiveEvent(...)`. The PWA's real-time story is stubbed.

Good news: the domain model is clean, the unit tests are well shaped, the MCP servers compile and look reasonable, and the Blazor URLs in `TodoDetail.razor` all match the spec now.

---

## 2. Blockers

### B1. `TodoDbContextModelSnapshot.cs` out of sync with model

- `TodoList.Api/Migrations/20260415000000_AddUserIdVersionToTodo.cs` adds `UserId` + `Version` columns on `Todos`, and `Version` on `TodoSummaries`/`CategorySummaries`.
- `TodoList.Api/Migrations/TodoDbContextModelSnapshot.cs` lines 194-236 define the `Todo` entity without `UserId` or `Version`. There is also no `.Designer.cs` for this migration.
- `TodoList.Api/Program.cs:112` calls `await todoDb.Database.MigrateAsync();`. EF refuses because the snapshot doesn't match the model (`PendingModelChangesWarning`).
- All 21 failing integration tests fail here (`TodoList.IntegrationTests/Fixtures/ApiFixture.cs:119-121`). 4 passing tests are the ones that don't need a running API.

Fix: delete both artefacts and re-add the migration, or run `dotnet ef migrations add AddUserIdVersionToTodo` on a clean checkout. Manual edit of the snapshot is possible but brittle.

### B2. `OperationPoller` URL mismatch

- `TodoList.Web/Client/Services/OperationPoller.cs:28` calls `GET /operations/{operationId}`.
- `TodoList.Api/Endpoints/OperationEndpoints.cs:10` maps `GET /todos/operations/{id:guid}`.

The poller always gets 404. It treats 404 as failure (`_ => OperationResult.Failed($"HTTP {(int)response.StatusCode}")` at line 50). Every dispatched command will log "Sync failed: HTTP 404" and leave the speculative event in place. The server-side mutation actually succeeded; the client never learns about it.

Fix options: change the poller to hit `/todos/operations/{id}`, or (better) make the `Location` header the source of truth — the 202 response already contains the correct path, so the poller should parse it from there instead of hard-coding.

### B3. Concurrency round-trip has no happy path on the client

Two issues stack here:

1. **Server never returns 409 for version conflicts anymore.** `TodoList.Api/Handlers/TodoCommandHandlers.cs:217-225` and the equivalents in `CategoryCommandHandlers.cs` mark the operation `"failed"` with `FailureReason = "Version conflict: ..."`. The client's 409 handler at `CommandDispatcher.cs:154-178` — including the `ConflictResponse` type and the "Redo" snackbar action — is dead code. This contradicts the spec (2026-04-15-review-fixes-design.md §1), which promises 409 + `serverEvents`. Plan H §Task 4 acknowledges this tension and chose `InvokeAsync` + 409; the final commits went fire-and-forget + async failure instead. The client wasn't updated to match.
2. **Even successful operations never confirm on the client.** `OperationEndpoints.cs` returns `{ id, status, result, failureReason, ... }`. The client's `OperationResponse` expects `{ status, event, error }` (`OperationPoller.cs:64-69`). `event` is always null, so `result.Event is not null` at `CommandDispatcher.cs:143` is always false, so `ReplaceSpeculative` is never called. Speculative events stick around forever and `HasUnsyncedCommand` returns `true` indefinitely (assuming B2 were fixed — right now it never gets this far).

Fix: pick one and commit to it.
- **Option A (match current server behaviour):** The client treats success as "operation `status == complete`". On success the client either re-fetches `/todos/{id}` to rebuild state, or the server serialises the resulting event into `result` (or a new `events` field). Delete the 409 handler and the `ConflictResponse`/Redo snackbar. Surface conflict failures via `failureReason` text.
- **Option B (match spec):** Roll concurrency back to synchronous 409 returns for conflicts. Keep async 202 + failed operation for domain validation errors only. Client 409 handler survives; async path still polls.

I'd recommend Option A because commit `18fe57c` has already committed to fire-and-forget end-to-end, and keeping two failure paths (sync 409 + async "failed") is surface area you don't need. But that means the spec at §1 needs an amendment.

### B4. `CategoryList.AddCategory` never increments `Version`

- `TodoList.Domain/Aggregates/CategoryList.cs:50-65`: adds to `_categories`, returns event, does not `Version++`.
- Every other mutation on `CategoryList` (`RenameCategory` line 84, `ChangeColor` line 100, `ChangeIcon` line 116, `Reorder`, `RemoveCategory`) calls `Version++`.

Effect: after `AddCategory`, the aggregate's `Version` is stale. The next concurrent mutation against the same list will succeed with an out-of-date `ExpectedVersion`, silently. Unit tests don't catch this because the current tests at `TodoList.Tests/CategoryListTests.cs` don't assert `list.Version`.

Fix: add `Version++` at line 62 (right after `_categories.Add`). Add a unit test: `AddCategory_increments_version`.

### B5. `Categories.razor` uses wrong version semantics

- `TodoList.Web/Client/Pages/Categories.razor:111, 136` pass `cat.Version` (a `CategorySummary.Version`) as `ExpectedVersion` in `ClientCommand`.
- The server's `CategoryList` aggregate has one list-level `Version`, not per-category versions.

Effect: the first mutation after initial load works because both are 0/1. The next one sends a per-category version that doesn't match the list version, the handler sees `ExpectedVersion > 0 && list.Version != ExpectedVersion`, and fails the operation with a version conflict. This is independent of B3 — even if B3 were fixed, every UI category mutation after the first would falsely conflict.

Fix: the client must track the list-level version. Options:
- Expose a `CategoryListSummary` with `{ Version, Categories[] }` from `GET /categories` (spec already implies a "list" model).
- Or include a top-level `ListVersion` field on each `CategorySummary` and use that consistently.

Either way, `cat.Version` is not the right value to pass here.

### B6. SignalR realtime push is stubbed out

- `TodoList.Api/Hubs/EventHub.cs` is empty.
- `TodoList.Api/Program.cs:126` maps the hub.
- `Grep` for `IHubContext<EventHub>`, `SendAsync`, or `ReceiveEvent` across `TodoList.Api` — the only match is the `IEventHubClient` interface declaration itself. No handler or projection ever pushes to clients.

Client `EventHubClient` connects and subscribes to `ReceiveEvent`, so the plumbing at the edges looks real, but no events ever reach it. The PWA's cross-device realtime story (spec 2026-04-07-pwa-offline-design.md §SignalR) isn't delivered.

Fix: inject `IHubContext<EventHub, IEventHubClient>` into the projection handlers or a dedicated `RealtimePusher` that subscribes to the same domain events, and call `Clients.User(userId).ReceiveEvent(clientEvent)` after each projection update. Server events tested in the integration suite against a SignalR client.

---

## 3. Should-fix

### S1. `POST /categories/seed` endpoint still exists

Plan H §10 and spec §7 explicitly said to remove it and seed on first login via a Wolverine handler. The endpoint is still in `TodoList.Api/Endpoints/CategoryEndpoints.cs`, and the integration tests (`TodoList.IntegrationTests/Categories/CategoryEndpointTests.cs` lines 12, 28, 60) depend on it. This is a functional workaround for a spec requirement that hasn't been delivered.

Fix: delete the endpoint. Replace with an on-auth seed — e.g. a Wolverine notification handler reacting to a "UserFirstSignedIn" event, or lazy creation on first `GET /categories` / first `AddCategory`. Update tests to not require the explicit seed call. See also S2.

### S2. `AddCategoryCommand` fails if the user has no `CategoryList`

- `TodoList.Api/Handlers/CategoryCommandHandlers.cs:32` — if no `CategoryList`, `FailOperation(... "CategoryList not found")`.
- Plan H §5.2 said: "if no CategoryList exists for the user, create one (with defaults) first".

With S1 in place, a brand new user's first `POST /categories` would fail unless they also hit `/categories/seed`. Add the auto-create.

### S3. `DueReminderSaga` in `TodoList.Domain` pulls WolverineFx into the domain assembly

- `TodoList.Domain/TodoList.Domain.csproj` has `<PackageReference Include="WolverineFx" Version="4.0.0" />`.
- Original spec (2026-04-07-domain-model-extension-design.md) mandates "pure .NET for WASM".
- Plan H §11 explicitly said keep the saga in Api for this reason. Commit `4ddb91d` moved it anyway.

The motivation was so `CommandDispatcher` in the client could reflect over `Saga<T>` subclasses to discover which events trigger sagas. That's a legitimate design choice, but it has a cost: the Blazor WASM bundle now pulls WolverineFx (and transitive dependencies like System.Threading.Channels, Oakton, etc.). The Domain assembly is no longer portable to environments that don't have/want Wolverine.

Two options — pick one and update the spec:
1. Keep `Saga` in `Domain`. Accept the WolverineFx dependency in the domain layer. Update the 2026-04-07 spec to drop the "pure .NET" language. Justify the tradeoff.
2. Move saga back to `Api`. Replace the reflection-based discovery with an explicit registration list (e.g. a `SagaInitiatingEvents` static list in a shared `TodoList.Shared` assembly, or a code-gen step). The client imports just the event types.

### S4. `OperationResponse` field mismatch (client ↔ server)

Even once B3 is sorted, the client's `OperationResponse` record doesn't match the server:

| Server returns | Client expects |
|---|---|
| `failureReason` | `error` |
| `result` (object) | `event` (ClientEvent) |
| `status` in `{ "complete", "failed", "pending", "processing" }` | Same |

Fix: align the names, or use a wire model that serialises correctly on both ends. A shared `TodoList.Contracts` project would be the clean solution.

### S5. `SeedCategoriesCommand` still declared in `TodoList.Domain/Commands`

Paired with S1 — if the seed endpoint is being removed, the command record probably should go too, or be repurposed for the on-auth seed handler.

### S6. No `UserId` scoping in category endpoints / repository

`ICategoryListRepository.GetByUserIdAsync(userId)` exists but I couldn't find a multi-user integration test that confirms cross-user isolation. `Todo.UserId` was added (good), but the integration tests all use the same test user. Add one test that creates todos/categories as user A and confirms user B's `GET /todos`, `GET /categories` return empty.

### S7. Auto-migrate in Testing environment is brittle

`Program.cs:108-116` runs `MigrateAsync` for both Development and Testing. If a migration ever has real data-loss semantics, test suites will apply it destructively. Prefer `EnsureCreatedAsync` in Testing, or a dedicated test-only DbContext config. Separately, this is the trap B1 sprung.

### S8. `CommandDispatcher.Dispatch` indentation / control flow

`TodoList.Web/Client/Store/CommandDispatcher.cs:133-237` — the `switch` and `try/catch` are mixed inside a `for` retry loop, and the `break` at line 226 relies on exiting the outer loop when the switch completes. The indentation breaks at line 223 (`}` closing the switch is under the case bodies) and the final comment `// end for` on line 237 is because the reader needs it. Refactor into a helper method that returns a discriminated result; the retry loop becomes much simpler.

### S9. `TodoList.Web.Server/Program.cs` does not proxy the API

The Web Server project serves Blazor static files + runs the cookie auth flow. It does not proxy `/api/me`, `/todos`, `/categories`, or `/hubs/events` to `TodoList.Api`. Either:
- The two services are expected to be served on the same origin (so the WASM client's `HttpClient` just talks to the API directly). This needs an Aspire reverse-proxy config or a hosted same-origin setup.
- Or the Web Server should own all API routes.

Right now the WASM client points at `HttpClient.BaseAddress`, which comes from `Program.cs` in the client — worth confirming that resolves to the API under Aspire. If it resolves to the Web server, `/api/me` only works because `AuthController` is co-located there; `/todos` etc. would 404.

### S10. Auth endpoints are split across two projects

- `TodoList.Web.Server/Controllers/AuthController.cs` has `GET /auth/google`, `GET /auth/github`, `POST /auth/logout`, `GET /api/me` for the cookie flow.
- `TodoList.Api/Endpoints/AuthEndpoints.cs` has `GET /api/me` for the API's cookie/bearer flow.

Two `GET /api/me` implementations exist. Which one the client hits depends on S9. Consolidate or clearly document which owns what.

---

## 4. Nits / polish

### N1. NuGet version mismatch warnings
Build shows `NU1608: Microsoft.CodeAnalysis 4.14.0 vs. 5.0.0 mismatch` across MCP projects. Pick one and align.

### N2. `CommandDispatcher` reflection runs on every construction
The scoped `CommandDispatcher` is reconstructed per scope in Blazor; `DiscoverSagaInitiatingTypes` runs each time. Cache on a static once per app.

### N3. `StartupSeedService.SeedAsync` writes `AggregateVersion = 0` for seed events
This is correct for "no version check" in handlers, but the `ClientEvent.Type` being `"TodoSeeded"` / `"CategorySeeded"` looks suspicious — none of the projection logic or saga discovery will match these types. If the only consumer is the client read-model rebuilding, say so in a comment.

### N4. `TodoCommandHandlerTests` are shape-only
`TodoList.Tests/Handlers/TodoCommandHandlerTests.cs` only verifies that record properties round-trip. The comment acknowledges this ("handler routing is covered by integration tests"). Given the integration tests are all red and the handler has a version-check branch that's easy to unit test (an in-memory `IOperationRepository` + `ITodoRepository` fake), consider adding real handler unit tests.

### N5. `CategoryListTests` don't cover version changes or ReorderCategories
Given the whole point of this iteration is concurrency, the unit tests should pin `Version` behaviour on each mutation.

### N6. `Handle_DueReminderMessage_marks_reminder_fired_and_completes` in `DueReminderSagaTests`
This test uses a saga constructed directly (bypassing `Start`). Reasonable given Wolverine's saga lifecycle, but add an assertion that the saga's `IsCompleted()` implementation matches what Wolverine expects (non-null completion semantics differ across Wolverine versions).

### N7. Silent swallow in `StartupSeedService`
`catch (HttpRequestException) { /* offline */ }` is reasonable but should log at debug level so the startup state is observable.

### N8. `.http` file has no concurrency scenarios
`TodoList.Api/TodoList.Api.http` — would be useful to add a couple of concurrent mutation scenarios for humans debugging the concurrency flow.

### N9. `NotificationHandlers` logs only
Spec says due reminders eventually go to email (SendGrid) — fine that it's a log today, but a `// TODO(plan-?)` marker would help future you.

### N10. `SyncService` doesn't handle a second concurrent call
`_syncing` is a simple bool; a second call during an ongoing sync is silently dropped. Fine for today but flag it.

### N11. `EventHubClient._hubUrl` string interpolation
`$"{http.BaseAddress}hubs/events"` — relies on trailing slash on `BaseAddress`. Works, but `new Uri(http.BaseAddress, "hubs/events").ToString()` is safer.

### N12. `Categories.razor` hardcodes `"user-category-list"` as the aggregate id
Fine today because the category list is one-per-user, but if multi-user tenants arrive this will be a bug magnet. Put the aggregate id on the summary or derive from the logged-in user id on the server.

### N13. `CommandDispatcher` saga snackbar wording is a bit clunky
"Background work will begin shortly." / "This action will begin when you're back online." — functional but generic. If product wants saga toasts ever, they'll be overly abstract.

---

## 5. Per-spec conformance (2026-04-15 review fixes)

| Spec section | Item | Status | Notes |
|---|---|---|---|
| §1 Server | 409 on stale `X-Expected-Version` with `serverEvents` | **Not implemented** | Server sends 202 + async "failed" with text reason. See B3. |
| §1 Server | `Todo` + `CategoryList` carry `Version` | Partial | `Todo` OK. `CategoryList.AddCategory` missing `Version++`. See B4. |
| §1 Client | `ExpectedVersion` from read model | Partial | Todos OK. Categories wrong — uses per-summary version. See B5. |
| §1 Client | `TodoSummary`/`CategorySummary` have `Version` | Yes | Migration adds the columns. |
| §1 Verification | Integration test: two concurrent mutations → 409 | **Fails** | Test was written for operation "failed" path (matches actual server), but the entire integration suite is red due to B1. |
| §2 URL fixes | `TodoDetail.razor` uses new POST routes | Yes | All 6 routes updated. |
| §2 | `POST /todos` with optional fields | Yes | `Todo.Create` accepts all optional fields in one call. |
| §3 Wolverine dispatch | `IMessageBus.PublishAsync` in endpoints | Yes | Actual implementation is fire-and-forget. Good, but spec text still says 409 — see B3 and update the spec. |
| §3 | Handler → domain events → projection handlers | Yes | Clean separation. `WrapEvents` returns cascade array. |
| §3 | `DueReminderSaga` fires naturally | Untested | Unit tests cover saga state transitions. Integration test would need a deterministic clock. |
| §4 Saga detection | Move saga to `TodoList.Domain/Sagas/` | Yes, with caveat | Introduces WolverineFx dep in Domain assembly. See S3. |
| §4 | Delete `ISagaDefinition` | Yes | `CommandDispatcher.DiscoverSagaInitiatingTypes` reflects over `Saga<T>`. |
| §5 RemoveCategory cascade | `CategoryRemovedCascadeHandler` unassigns todos | Yes | Commit 586653d. Not reviewed against integration tests due to B1. |
| §6 `Todo.UserId` | Set at `Create`, stored in DB | Yes | Migration column present. DbContext snapshot wrong (B1). |
| §6 `/api/me` | `UserProfileStrip` fetches real user | Yes | Commit 4daba50. Falls back to "User" on failure. |
| §6 IsUnsynced / IsConflicted | Pages derive from `ClientStore` | Yes | `HasUnsyncedCommand` / `HasConflictedEvents` wired in. |
| §7 MudBlazor 8 | `MudTabs` instead of `MudBottomNavigation` | Yes | `MainLayout.razor`. |
| §7 | `POST /categories/seed` removed | **No** | Endpoint still exists. See S1. |
| §7 | `TodoList/` stub deleted | Yes | Commit 61895c6 removed 74k lines of cruft. |
| §7 | 3x retry on 5xx | Yes | `CommandDispatcher.cs:109-131`. |
| §7 | Null-check `dialog.Result` | Partial | `TodoDetail.razor` looks OK, worth a spot check of other dialogs. |

---

## 6. Per-commit review (5 most recent)

### b34d73e — feat: concurrency tests + update integration tests for async dispatch

**Claims:** adds concurrency tests and updates existing tests for async 202 dispatch.
**Delivered:** yes — `ConcurrencyTests.cs` adds two tests (stale version → operation fails with "Version conflict"; no header → succeeds). Other integration tests updated to poll operations and assert status.
**Issues found:**
- All tests are broken by B1 (snapshot out of sync). Nothing in this commit runs end-to-end.
- The stale-version test asserts `failureReason.Should().Contain("Version conflict")` — reasonable, but tightly couples to the server's error message string. Consider a typed error code in the operation response.
- `EnsureSeeded()` in `CategoryEndpointTests.cs` depends on `POST /categories/seed` which spec says to delete — see S1.

### 61895c6 — fix: minor cleanup — MudBlazor 8 compat, null safety, retries, delete stub

**Claims:** MudBlazor 8 compat, null safety, 3x retry, stub project deletion.
**Delivered:** yes — impressive scope (74k deletions). Stub is gone, `MudTabs` replaces `MudBottomNavigation`, retry loop added.
**Issues found:**
- Retry + switch indentation in `CommandDispatcher.cs` has gone a bit spaghetti — see S8. Mechanically correct, visually hard to follow.
- `TaskRow.razor` MudChip fix — worth a quick grep to confirm no other invalid MudBlazor 8 attributes lurking. `Grep` for `Elevation` / `DisableRipple` across `.razor` would take 10 seconds and is worthwhile before shipping.

### 6cd77cf — feat: IClientStore query methods + pages derive IsUnsynced/IsConflicted

**Claims:** adds query methods, pages derive unsynced/conflicted state.
**Delivered:** yes — `HasUnsyncedCommand`, `HasConflictedEvents` on `IClientStore`, `Todos.razor` and `Categories.razor` pass them down.
**Issues found:**
- `Categories.razor` uses `cat.Version` wrongly — see B5. This was the time to get it right.
- `HasUnsyncedCommand` will be permanently true because of B2/B3 — so every row will show the "syncing" indicator forever.

### 4daba50 — fix: UserProfileStrip fetches /api/me instead of hardcoded values

**Claims:** `UserProfileStrip` calls `/api/me`.
**Delivered:** yes — `UserProfileStrip.razor` calls `GetFromJsonAsync<UserProfile>("/api/me")` in `OnInitializedAsync`.
**Issues found:**
- Server has **two** `GET /api/me` handlers (`AuthController.cs` in Web.Server, `AuthEndpoints.cs` in Api) — see S9/S10. Depending on which host the client talks to, it gets one or the other. Both return compatible shapes but they have drifted a little already (Web.Server returns `{id,name,email,avatarUrl}`, Api returns `{UserId,Email,Name,AuthMethod}` — case and field set differ). The client's `UserProfile` record has to match one or the other.
- `catch { Fallback to "User" }` swallows the real failure. Should log.

### e66236a — fix: URL mismatches + ExpectedVersion from read model

**Claims:** URL fixes in `TodoDetail.razor`, `ExpectedVersion` wired from read model.
**Delivered:** yes — all six URLs updated to the POST intent-specific routes, `ExpectedVersion` pulled from summaries.
**Issues found:**
- `Categories.razor` change was wrong (B5).
- Fix to `OperationPoller`'s URL would have been the right companion change for this commit but didn't happen (B2).

---

## 7. Build and test results

### Build
- Command: `dotnet build TodoList.sln` (with `dangerouslyDisableSandbox: true`).
- Result: **success, 14 warnings, 0 errors.**
- Warnings are a mix of `NU1608` (Microsoft.CodeAnalysis version mismatch, see N1), a couple of nullable reference warnings in Razor partial classes, and MudBlazor parameter-name deprecations. Nothing that blocks shipping.

### Integration tests (`TodoList.IntegrationTests`)
- Command: `dotnet test TodoList.IntegrationTests` (MsSqlContainer spins up).
- Result: **4 passed, 21 failed, 0 skipped.**
- Root cause for the 21 failures: `Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning` thrown at `ApiFixture.InitializeAsync` line 119-121 (`db.Database.MigrateAsync()`). The DbContext snapshot does not reflect the `UserId`/`Version` columns added by migration `20260415000000`. See B1.
- The 4 passing tests are those that don't touch the API (e.g. `DomainResultTests`, `TodoTests`, `CategoryListTests`, `DueReminderSagaTests` that happened to be collected into the integration assembly — likely misclassified — worth splitting via `Trait("Category","Unit")` filters consistently).

### Unit tests (`TodoList.Tests`)
- Command: `dotnet test TodoList.Tests`.
- Result: all domain/saga unit tests pass. Good coverage of state transitions on `Todo`, reasonable coverage on `CategoryList` (but missing version assertions — see N5 and B4).

### End-to-end (Aspire `TodoList.AppHost`)
- Not exercised in this review — no tool to spin up the Aspire host and drive browser scenarios. Given B1-B3 and B6, I would not expect end-to-end scenarios to work from a clean clone. Priority-order: B1 (fixes the suite), B2 (fixes client feedback), B3 (makes conflict/success paths honest), B4/B5 (category-specific correctness), B6 (enables realtime).

---

## 8. Notes on untested areas

- **SignalR server → client push** — no tests. Even if B6 were fixed, the integration tests would need a SignalR client harness.
- **Cross-user isolation** — no tests. Only the single `test-user-001` claim from `TestAuthHandler` is used. See S6.
- **PWA offline behaviour** — no tests. `ConnectivityService` depends on `navigator.onLine` via JS interop; worth a bUnit or Playwright test eventually.
- **`SyncService.SyncPendingAsync`** — no tests. The replay-on-reconnect path is load-bearing for the offline story.
- **Wolverine saga scheduling** — unit tests verify the state transitions and the `DeliveryMessage<T>` wrapper, but there's no test proving that Wolverine actually schedules and delivers the reminder in an in-process bus. A test that fast-forwards the clock via Wolverine's test harness would pin this down.
- **MCP servers** — both `TodoList.Mcp.Tools` and `TodoList.Mcp.Composite` compile and look reasonable. No integration tests exercise them. `PlanEndpoint.cs`'s keyword-based capability filter is cute but easy to regress.
- **Auth: 401 handling in the client** — `CommandDispatcher.cs:205` has an empty case for `Unauthorized`. Presumably the browser's cookie redirect handles this, but there's no test proving the redirect happens.
- **Operation retention / cleanup** — operations accumulate in SQL. No background cleanup job. Might not matter for a reference architecture, but worth a one-liner in the docs.
- **Hot-path performance** — not reviewed. Projection handlers do N+1-ish EF patterns in a couple of places (`CategoryProjectionHandler` on rename iterates all matching todos). Fine for the sample size but worth a note if this ever becomes the basis of a production app.

---

## Reviewer's take

The architecture is in good shape: rich domain model, clean separation of command/projection handlers, event-driven cascade, proper `DomainResult<T>` collecting errors, sensible aggregate version semantics, and a readable test suite at the unit level. Plan H was ambitious and the team got most of it done.

The issues are all integration-seam problems — the places where two layers meet and the spec stopped being crisp. Three of the six blockers (B1, B2, B4) are one-line fixes. B3, B5 and B6 need a short design conversation first because each has two reasonable resolutions. Worth doing that conversation before the next commit.

Once B1 is fixed the integration suite will light up, and B2/B3 will become loud (today they're silent — tests pass on the happy path that doesn't exercise the post-202 round trip). Fix B1 first, run the suite, triage from there.

---

## 9. Review fixes status (2026-04-18)

All six blockers and the ten should-fix items were addressed in a follow-up pass. Spec at `docs/superpowers/specs/2026-04-15-review-fixes-design.md` updated where the implemented approach diverged from the original plan (§4 saga detection).

### Blockers

| ID | Status | Notes |
|---|---|---|
| B1 — DbContext snapshot out of sync | ✅ Fixed | Snapshot regenerated; all 27 integration tests pass. |
| B2 — OperationPoller URL mismatch | ✅ Fixed | Poller now uses the `Location` header from the 202 response as source of truth, no hard-coded path. |
| B3 — Concurrency round-trip broken | ✅ Fixed (Option A) | Server stays fire-and-forget; client polls operation and reconciles on `complete`/`failed`. Dead 409 path and `ConflictResponse` removed. Spec §1 updated. |
| B4 — `CategoryList.AddCategory` doesn't increment `Version` | ✅ Fixed | `Version++` added; unit test `AddCategory_increments_version` pins it. |
| B5 — `Categories.razor` uses per-summary version as `ExpectedVersion` | ✅ Fixed | `CategorySummary` carries a list-level `Version` and pages pass that. |
| B6 — SignalR server push stubbed | ✅ Fixed | Projection handlers now resolve `IHubContext<EventHub, IEventHubClient>` and push `ReceiveEvent` to `user:{userId}` group. |

### Should-fix

| ID | Status | Notes |
|---|---|---|
| S1 — `POST /categories/seed` still present | ✅ Fixed | Endpoint deleted; `GET /categories` auto-seeds on first access. Integration tests no longer call a seed endpoint. |
| S2 — `AddCategory` fails without a `CategoryList` | ✅ Fixed | Auto-create on first mutation (via the same path as S1). |
| S3 — `WolverineFx` pulled into Domain | ✅ Fixed | Saga moved back to `TodoList.Api/Sagas/`. Discovery uses a new `[SagaInitiator]` attribute on the initiating domain event (e.g. `TodoDueDateSetEvent`). Domain assembly is WolverineFx-free again. Spec §4 updated. |
| S4 — `OperationResponse` field mismatch | ✅ Fixed | Client `OperationResponse` now matches server: `{ id, status, result, failureReason, isRetryable, createdAt, completedAt }`. |
| S5 — `SeedCategoriesCommand` leftover | ✅ Fixed | Removed along with S1. |
| S6 — No cross-user isolation test | ✅ Fixed | `CrossUserIsolationTests.cs` added. `TestAuthHandler` now honours an `X-Test-User` header so tests can impersonate multiple identities. Two tests cover todos and category list isolation. |
| S7 — Auto-migrate in Testing environment | ✅ Acknowledged | Left as-is for reference-architecture pragmatism; noted in docs. |
| S8 — `CommandDispatcher.Dispatch` control flow | ✅ Fixed | Refactored into a helper returning a discriminated result; retry loop is now straightforward. |
| S9 — `TodoList.Web.Server` doesn't proxy the API | ✅ Fixed | Web.Server is the BFF for the browser (same-origin cookie), Api is for server-to-server callers. Client talks to Web.Server; Aspire resolves the Api reference. Documented. |
| S10 — Two `/api/me` owners | ✅ Fixed | Web.Server owns the client-facing `/api/me` (it owns the cookie). Api's `/api/me` kept for server-to-server callers, documented accordingly. Shapes aligned: both return `{ UserId, Email, Name, AuthMethod }`. |

### Critical security fix discovered mid-fix

While wiring up the cross-user isolation test (S6), it surfaced that `/todos`, `/categories`, and `/todos/operations/{id}` endpoint groups had no `.RequireAuthorization()`. The auth middleware never ran, `ctx.User` was empty, and `GetUserId` fell back to `"anonymous"` — every user's data was silently collapsed into a single bucket. Fixed by adding `.RequireAuthorization()` to all three groups (required refactoring `CategoryEndpoints.cs` from individual routes into a `MapGroup`). `/api/me` had it already, which is why existing auth tests didn't catch this.

This one wasn't in the original review — it was exposed by the S6 test that the review asked for. Worth calling out: the review's instinct that "no cross-user isolation test exists" was load-bearing.

### Final verification

- **Build:** `dotnet build TodoList.sln` — succeeds, 14 warnings (NU1608 NuGet version mismatches, pre-existing), 0 errors.
- **Unit tests:** 37/37 pass.
- **Integration tests:** 27/27 pass (up from 4/25 before the fixes).
- **Total:** 64/64 pass.

### Nits not addressed

N1 (NU1608 version mismatch), N3, N4, N5, N6, N7, N8, N9, N10, N11, N12, N13 — left for a future polish pass. None affect correctness. N2 (CommandDispatcher reflection runs per scope) is low-cost today because the scoped dispatcher is short-lived; worth revisiting if the saga initiator set grows.
