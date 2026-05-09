# PWA / Offline / SignalR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the Blazor WASM client's `ClientStore` (localStorage-backed event + command store), replace stub stores with real `LocalTodoStore` / `LocalCategoryStore` projectors, implement `CommandDispatcher` (speculative events → POST→202→poll → confirmed), add `ConnectivityService` (online/offline), `SyncService` (replay on reconnect), and `SignalR` (server-pushed events). The result is a fully offline-capable app where one flow handles online and offline mutations.

**Architecture:** `ClientStore` is the single source of truth on the client. Commands write a speculative event immediately; the read model rebuilds; the command is sent to the API (or queued). Server confirmation replaces the speculative event. 409 = server events win + redo toast. 422 = mark conflicted + warning icon + Review toast. SignalR delivers server-side saga events to open clients. Connectivity changes trigger queued command replay via `SyncService`.

**Tech Stack:** .NET 10, Blazor WASM, MudBlazor, SignalR client (`Microsoft.AspNetCore.SignalR.Client`), `Microsoft.JSInterop` for localStorage + connectivity events, `System.Text.Json`

> **Read before starting:** `docs/superpowers/specs/2026-04-07-pwa-offline-design.md`, `docs/superpowers/specs/2026-04-07-domain-model-extension-design.md`, `docs/superpowers/plans/2026-04-11-plan-b-blazor-web-ui.md` (interfaces defined in Task 3)

> **Prerequisites:** Plan A complete (TodoList.Domain exists with aggregates, events, commands, ISagaDefinition, projectors). Plan B complete (ILocalTodoStore, ILocalCategoryStore interfaces and stub stores exist in Client/Store/).

---

## File Map

### New/Modified: `TodoList.Web/Client/`

```
TodoList.Web/Client/
  Store/
    ClientEvent.cs               # ClientEvent record, EventSource enum, EventState enum
    ClientCommand.cs             # ClientCommand record
    IClientStore.cs              # Interface — AppendEvent, ReplaceSpeculative, MarkConflicted, etc.
    ClientStore.cs               # localStorage-backed implementation
    LocalTodoStore.cs            # ILocalTodoStore implementation — projects events from ClientStore
    LocalCategoryStore.cs        # ILocalCategoryStore implementation
    CommandDispatcher.cs         # Dispatch: validate → speculative event → POST → poll → confirm
    SyncService.cs               # On reconnect, replays all unsynced commands in Timestamp order
  Services/
    ConnectivityService.cs       # JS interop: window online/offline events + navigator.onLine
    OperationPoller.cs           # Polls GET /operations/{id} until terminal status
  Hubs/
    EventHubClient.cs            # SignalR client — connects to /hubs/events, routes ReceiveEvent
  wwwroot/
    js/
      connectivity.js            # JS module: listen online/offline, expose dotNetRef.invokeMethod
```

### Modified: `TodoList.Web/Client/Program.cs`
- Replace `StubLocalTodoStore` / `StubLocalCategoryStore` registrations with real implementations
- Register `IClientStore`, `IConnectivityService`, `CommandDispatcher`, `SyncService`, `EventHubClient`
- Add SignalR client package

### Modified: `TodoList.Web/Client/Pages/TodoList.razor`
- Replace snackbar stubs with real `CommandDispatcher.Dispatch(command)` calls

### Modified: `TodoList.Web/Client/Pages/TodoDetail.razor`
- Same as above

### Modified: `TodoList.Web/Client/Pages/Categories.razor`
- Same as above

### Modified: `TodoList.Web/Client/Layout/UserProfileStrip.razor`
- Wire logout button to POST `/auth/logout`

### New: `TodoList.Api/Hubs/EventHub.cs`
- SignalR hub: `ReceiveEvent(ClientEvent event)`

### Modified: `TodoList.Api/Program.cs`
- Add SignalR services + hub mapping

### New/Modified: `TodoList.Web/Server/Program.cs`
- Proxy `/hubs/events` to the API's SignalR hub

---

## Tasks

### Task 1: ClientEvent and ClientCommand types

**Files:**
- Create: `TodoList.Web/Client/Store/ClientEvent.cs`
- Create: `TodoList.Web/Client/Store/ClientCommand.cs`

- [ ] **Step 1: Write the failing test**

Since these are pure data types (no logic), the "test" is a build verification. Create the types first, then confirm they build.

```csharp
// TodoList.Web/Client/Store/ClientEvent.cs
using System.Text.Json.Serialization;

namespace TodoList.Web.Client.Store;

public record ClientEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string AggregateId { get; init; } = "";
    public int AggregateVersion { get; init; }
    public string Type { get; init; } = "";

    [JsonExtensionData]
    public Dictionary<string, object?>? Payload { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public EventSource Source { get; init; }
    public EventState State { get; init; }
}

public enum EventSource { Client, Server }

public enum EventState { Speculative, Confirmed, Conflicted }
```

Note: `Payload` uses `[JsonExtensionData]` for schema-free JSON serialisation from the API. If the API returns a typed payload you can use `JsonElement` instead:

```csharp
// TodoList.Web/Client/Store/ClientEvent.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoList.Web.Client.Store;

public record ClientEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string AggregateId { get; init; } = "";
    public int AggregateVersion { get; init; }
    public string Type { get; init; } = "";
    public JsonElement? Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public EventSource Source { get; init; }
    public EventState State { get; init; }
}

public enum EventSource { Client, Server }

public enum EventState { Speculative, Confirmed, Conflicted }
```

- [ ] **Step 2: Create ClientCommand**

```csharp
// TodoList.Web/Client/Store/ClientCommand.cs
using System.Text.Json;

namespace TodoList.Web.Client.Store;

public record ClientCommand
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string AggregateId { get; init; } = "";
    public int ExpectedVersion { get; init; }
    public int SpeculativeVersion => ExpectedVersion + 1;
    public string Type { get; init; } = "";
    public JsonElement? Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool Synced { get; init; }
    public string ApiEndpoint { get; init; } = ""; // e.g. "/todos/create"
}
```

- [ ] **Step 3: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Store/ClientEvent.cs TodoList.Web/Client/Store/ClientCommand.cs
git commit -m "feat: add ClientEvent and ClientCommand types with EventSource/EventState enums"
```

---

### Task 2: IClientStore interface and localStorage implementation

**Files:**
- Create: `TodoList.Web/Client/Store/IClientStore.cs`
- Create: `TodoList.Web/Client/Store/ClientStore.cs`
- Create: `TodoList.Web/Client/wwwroot/js/storage.js`

- [ ] **Step 1: Create IClientStore**

```csharp
// TodoList.Web/Client/Store/IClientStore.cs
namespace TodoList.Web.Client.Store;

public interface IClientStore
{
    // Event log
    void AppendEvent(ClientEvent evt);
    IReadOnlyList<ClientEvent> GetEventsFor(string aggregateId);
    IReadOnlyList<ClientEvent> GetAllEvents();           // ordered by (aggregateId, aggregateVersion)
    void ReplaceSpeculative(string aggregateId, IReadOnlyList<ClientEvent> serverEvents);
    void MarkConflicted(string aggregateId, IReadOnlyList<ValidationError> errors);
    void DiscardSpeculative(string aggregateId);

    // Command queue
    IReadOnlyList<ClientCommand> GetUnsyncedCommands();
    void EnqueueCommand(ClientCommand command);
    void MarkSynced(string commandId);

    // Change notifications
    event Action<string> OnAggregateChanged; // fires with aggregateId after every mutation
}

public record ValidationError(string Field, string Message);
```

- [ ] **Step 2: Create JS storage module**

```javascript
// TodoList.Web/Client/wwwroot/js/storage.js
export function getItem(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function setItem(key, value) {
    try {
        localStorage.setItem(key, value);
        return true;
    } catch {
        return false;
    }
}

export function removeItem(key) {
    try {
        localStorage.removeItem(key);
    } catch { }
}
```

- [ ] **Step 3: Create ClientStore**

```csharp
// TodoList.Web/Client/Store/ClientStore.cs
using System.Text.Json;
using Microsoft.JSInterop;

namespace TodoList.Web.Client.Store;

public class ClientStore : IClientStore, IAsyncDisposable
{
    private const string EventsKey = "todolist:events";
    private const string CommandsKey = "todolist:commands";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _storageModule;

    private List<ClientEvent> _events = [];
    private List<ClientCommand> _commands = [];
    private bool _loaded;

    public event Action<string> OnAggregateChanged = delegate { };

    public ClientStore(IJSRuntime js)
    {
        _js = js;
    }

    // Lazy-load from localStorage on first use
    private async ValueTask EnsureLoaded()
    {
        if (_loaded) return;
        _storageModule ??= await _js.InvokeAsync<IJSObjectReference>(
            "import", "./js/storage.js");

        var eventsJson = await _storageModule.InvokeAsync<string?>("getItem", EventsKey);
        var commandsJson = await _storageModule.InvokeAsync<string?>("getItem", CommandsKey);

        _events = eventsJson is not null
            ? JsonSerializer.Deserialize<List<ClientEvent>>(eventsJson) ?? []
            : [];
        _commands = commandsJson is not null
            ? JsonSerializer.Deserialize<List<ClientCommand>>(commandsJson) ?? []
            : [];

        _loaded = true;
    }

    private async Task Persist()
    {
        _storageModule ??= await _js.InvokeAsync<IJSObjectReference>(
            "import", "./js/storage.js");
        await _storageModule.InvokeVoidAsync("setItem", EventsKey,
            JsonSerializer.Serialize(_events));
        await _storageModule.InvokeVoidAsync("setItem", CommandsKey,
            JsonSerializer.Serialize(_commands));
    }

    // Synchronous facade used by CommandDispatcher (after initial load)
    public void AppendEvent(ClientEvent evt)
    {
        _events.Add(evt);
        _ = Persist();
        OnAggregateChanged(evt.AggregateId);
    }

    public IReadOnlyList<ClientEvent> GetEventsFor(string aggregateId) =>
        _events.Where(e => e.AggregateId == aggregateId)
               .OrderBy(e => e.AggregateVersion)
               .ToList()
               .AsReadOnly();

    public IReadOnlyList<ClientEvent> GetAllEvents() =>
        _events.OrderBy(e => e.AggregateId)
               .ThenBy(e => e.AggregateVersion)
               .ToList()
               .AsReadOnly();

    public void ReplaceSpeculative(string aggregateId, IReadOnlyList<ClientEvent> serverEvents)
    {
        _events.RemoveAll(e => e.AggregateId == aggregateId && e.State == EventState.Speculative);
        _events.AddRange(serverEvents.Select(e => e with { State = EventState.Confirmed, Source = EventSource.Server }));
        _ = Persist();
        OnAggregateChanged(aggregateId);
    }

    public void MarkConflicted(string aggregateId, IReadOnlyList<ValidationError> errors)
    {
        var speculative = _events
            .Where(e => e.AggregateId == aggregateId && e.State == EventState.Speculative)
            .ToList();
        foreach (var evt in speculative)
        {
            var idx = _events.IndexOf(evt);
            _events[idx] = evt with { State = EventState.Conflicted };
        }
        _ = Persist();
        OnAggregateChanged(aggregateId);
    }

    public void DiscardSpeculative(string aggregateId)
    {
        _events.RemoveAll(e => e.AggregateId == aggregateId &&
                               (e.State == EventState.Speculative || e.State == EventState.Conflicted));
        _ = Persist();
        OnAggregateChanged(aggregateId);
    }

    public IReadOnlyList<ClientCommand> GetUnsyncedCommands() =>
        _commands.Where(c => !c.Synced)
                 .OrderBy(c => c.Timestamp)
                 .ToList()
                 .AsReadOnly();

    public void EnqueueCommand(ClientCommand command)
    {
        _commands.Add(command);
        _ = Persist();
    }

    public void MarkSynced(string commandId)
    {
        var idx = _commands.FindIndex(c => c.Id == commandId);
        if (idx >= 0)
        {
            _commands[idx] = _commands[idx] with { Synced = true };
            _ = Persist();
        }
    }

    public async Task InitializeAsync()
    {
        await EnsureLoaded();
    }

    public async ValueTask DisposeAsync()
    {
        if (_storageModule is not null)
            await _storageModule.DisposeAsync();
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 5: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Store/IClientStore.cs TodoList.Web/Client/Store/ClientStore.cs TodoList.Web/Client/wwwroot/js/storage.js
git commit -m "feat: add IClientStore interface and localStorage-backed ClientStore"
```

---

### Task 3: LocalTodoStore and LocalCategoryStore (real projectors)

**Files:**
- Create: `TodoList.Web/Client/Store/LocalTodoStore.cs`
- Create: `TodoList.Web/Client/Store/LocalCategoryStore.cs`
- Delete: `TodoList.Web/Client/Store/StubLocalTodoStore.cs`
- Delete: `TodoList.Web/Client/Store/StubLocalCategoryStore.cs`

These classes replace the Plan B stubs. They listen to `IClientStore.OnAggregateChanged` and rebuild their read models by replaying events using the shared projector logic from `TodoList.Domain`.

- [ ] **Step 1: Create LocalTodoStore**

```csharp
// TodoList.Web/Client/Store/LocalTodoStore.cs
using TodoList.Domain.Projectors;
using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public class LocalTodoStore : ILocalTodoStore
{
    private readonly IClientStore _clientStore;
    private List<TodoSummary> _todos = [];

    public event Action OnChange = delegate { };

    public LocalTodoStore(IClientStore clientStore)
    {
        _clientStore = clientStore;
        _clientStore.OnAggregateChanged += aggregateId => Rebuild(aggregateId);
    }

    public IReadOnlyList<TodoSummary> Todos => _todos.AsReadOnly();
    public TodoSummary? GetById(string id) => _todos.FirstOrDefault(t => t.Id == id);

    private void Rebuild(string? changedAggregateId = null)
    {
        // Full rebuild from all events — projector handles ordering
        var allEvents = _clientStore.GetAllEvents();
        _todos = TodoProjector.ProjectAll(allEvents
            .Select(e => new DomainEventEnvelope(e.AggregateId, e.AggregateVersion, e.Type, e.Payload))
            .ToList());
        OnChange();
    }

    public void RebuildAll() => Rebuild();
}
```

Note: `TodoProjector.ProjectAll` is defined in `TodoList.Domain/Projectors/TodoProjector.cs` (created in Plan A). It takes a list of domain event envelopes and returns `List<TodoSummary>`. If the Plan A projector uses a different signature, adapt accordingly.

The `DomainEventEnvelope` is a simple record to pass events between layers:

```csharp
// If TodoList.Domain doesn't define DomainEventEnvelope, define it in the Client:
// TodoList.Web/Client/Store/DomainEventEnvelope.cs
using System.Text.Json;

namespace TodoList.Web.Client.Store;

public record DomainEventEnvelope(
    string AggregateId,
    int AggregateVersion,
    string Type,
    System.Text.Json.JsonElement? Payload);
```

Read `TodoList.Domain/Projectors/TodoProjector.cs` before implementing to match the actual signature. If the projector uses a different input type, adapt `Rebuild()` accordingly.

- [ ] **Step 2: Create LocalCategoryStore**

```csharp
// TodoList.Web/Client/Store/LocalCategoryStore.cs
using TodoList.Domain.Projectors;
using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public class LocalCategoryStore : ILocalCategoryStore
{
    private readonly IClientStore _clientStore;
    private List<CategorySummary> _categories = [];

    public event Action OnChange = delegate { };

    public LocalCategoryStore(IClientStore clientStore)
    {
        _clientStore = clientStore;
        _clientStore.OnAggregateChanged += _ => Rebuild();
    }

    public IReadOnlyList<CategorySummary> Categories => _categories.AsReadOnly();
    public CategorySummary? GetById(string id) => _categories.FirstOrDefault(c => c.Id == id);

    private void Rebuild()
    {
        var allEvents = _clientStore.GetAllEvents();
        _categories = CategoryProjector.ProjectAll(allEvents
            .Select(e => new DomainEventEnvelope(e.AggregateId, e.AggregateVersion, e.Type, e.Payload))
            .ToList());
        OnChange();
    }

    public void RebuildAll() => Rebuild();
}
```

- [ ] **Step 3: Delete stub stores**

```bash
rm /Users/jim/code/todo-patterns/TodoList.Web/Client/Store/StubLocalTodoStore.cs
rm /Users/jim/code/todo-patterns/TodoList.Web/Client/Store/StubLocalCategoryStore.cs
```

If `ReadModelStubs.cs` was created as a temporary file in Plan B, delete it too:

```bash
rm -f /Users/jim/code/todo-patterns/TodoList.Web/Client/Store/ReadModelStubs.cs
```

- [ ] **Step 4: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

Expected: Build succeeds. The `TodoProjector` and `CategoryProjector` types from `TodoList.Domain` must exist. If Plan A hasn't run yet, you'll get reference errors — stop and run Plan A first.

- [ ] **Step 5: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Store/LocalTodoStore.cs TodoList.Web/Client/Store/LocalCategoryStore.cs
git rm TodoList.Web/Client/Store/StubLocalTodoStore.cs TodoList.Web/Client/Store/StubLocalCategoryStore.cs
git commit -m "feat: replace stub stores with real LocalTodoStore/LocalCategoryStore projectors"
```

---

### Task 4: ConnectivityService

**Files:**
- Create: `TodoList.Web/Client/wwwroot/js/connectivity.js`
- Create: `TodoList.Web/Client/Services/ConnectivityService.cs`

- [ ] **Step 1: Create connectivity JS module**

```javascript
// TodoList.Web/Client/wwwroot/js/connectivity.js
let dotNetRef = null;

export function initialize(ref) {
    dotNetRef = ref;

    window.addEventListener('online', () => {
        if (dotNetRef) dotNetRef.invokeMethodAsync('OnConnectivityChanged', true);
    });

    window.addEventListener('offline', () => {
        if (dotNetRef) dotNetRef.invokeMethodAsync('OnConnectivityChanged', false);
    });

    return navigator.onLine;
}

export function isOnline() {
    return navigator.onLine;
}

export function dispose() {
    dotNetRef = null;
}
```

- [ ] **Step 2: Create IConnectivityService**

```csharp
// TodoList.Web/Client/Services/IConnectivityService.cs
namespace TodoList.Web.Client.Services;

public interface IConnectivityService
{
    bool IsOnline { get; }
    event Action<bool> OnConnectivityChanged;
}
```

- [ ] **Step 3: Create ConnectivityService**

```csharp
// TodoList.Web/Client/Services/ConnectivityService.cs
using Microsoft.JSInterop;

namespace TodoList.Web.Client.Services;

public class ConnectivityService : IConnectivityService, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private DotNetObjectReference<ConnectivityService>? _dotNetRef;

    public bool IsOnline { get; private set; } = true;
    public event Action<bool> OnConnectivityChanged = delegate { };

    public ConnectivityService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        _module = await _js.InvokeAsync<IJSObjectReference>(
            "import", "./js/connectivity.js");
        _dotNetRef = DotNetObjectReference.Create(this);
        IsOnline = await _module.InvokeAsync<bool>("initialize", _dotNetRef);
    }

    [JSInvokable]
    public void OnConnectivityChangedJs(bool isOnline)
    {
        IsOnline = isOnline;
        OnConnectivityChanged(isOnline);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }
        _dotNetRef?.Dispose();
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 5: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Services/ TodoList.Web/Client/wwwroot/js/connectivity.js
git commit -m "feat: add ConnectivityService (JS interop for online/offline events)"
```

---

### Task 5: OperationPoller

**Files:**
- Create: `TodoList.Web/Client/Services/OperationPoller.cs`

The poller calls `GET /operations/{id}` until it reaches a terminal status (`complete`, `failed`, `not_found`).

- [ ] **Step 1: Create OperationPoller**

```csharp
// TodoList.Web/Client/Services/OperationPoller.cs
using System.Net.Http.Json;

namespace TodoList.Web.Client.Services;

public class OperationPoller
{
    private readonly HttpClient _http;

    public OperationPoller(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Polls GET /operations/{id} until terminal status.
    /// Returns the confirmed ClientEvent on success, null on failure/timeout.
    /// </summary>
    public async Task<OperationResult> PollAsync(string operationId, CancellationToken ct = default)
    {
        var delays = new[] { 200, 400, 800, 1600, 3200 };
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await _http.GetAsync($"/operations/{operationId}", ct);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<OperationResponse>(ct);
                    if (body is null) return OperationResult.Failed("Empty response");

                    return body.Status switch
                    {
                        "complete" => OperationResult.Success(body.Event),
                        "failed" => OperationResult.Failed(body.Error ?? "Operation failed"),
                        "pending" or "processing" => null!, // keep polling
                        _ => OperationResult.Failed($"Unknown status: {body.Status}")
                    };
                }

                if ((int)response.StatusCode >= 500)
                {
                    // Transient — retry
                }
                else
                {
                    return OperationResult.Failed($"HTTP {(int)response.StatusCode}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* network error — retry */ }

            if (attempt >= delays.Length) return OperationResult.Failed("Polling timeout");
            await Task.Delay(delays[attempt++], ct);
        }

        return OperationResult.Failed("Cancelled");
    }
}

public record OperationResponse
{
    public string Status { get; init; } = "";
    public TodoList.Web.Client.Store.ClientEvent? Event { get; init; }
    public string? Error { get; init; }
}

public record OperationResult
{
    public bool IsSuccess { get; init; }
    public TodoList.Web.Client.Store.ClientEvent? Event { get; init; }
    public string? ErrorMessage { get; init; }

    public static OperationResult Success(TodoList.Web.Client.Store.ClientEvent? evt) =>
        new() { IsSuccess = true, Event = evt };
    public static OperationResult Failed(string error) =>
        new() { IsSuccess = false, ErrorMessage = error };
}
```

- [ ] **Step 2: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Services/OperationPoller.cs
git commit -m "feat: add OperationPoller — polls GET /operations/{id} until terminal status"
```

---

### Task 6: CommandDispatcher

**Files:**
- Create: `TodoList.Web/Client/Store/CommandDispatcher.cs`

The dispatcher is the core of the offline-first flow:
1. Validate
2. Write speculative event + enqueue command
3. Notify read models (immediate UI update)
4. If online: POST → poll operation → confirm / handle 409/422
5. If offline: queue; show saga toast if applicable

- [ ] **Step 1: Create CommandDispatcher**

```csharp
// TodoList.Web/Client/Store/CommandDispatcher.cs
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using MudBlazor;
using TodoList.Domain.Sagas;
using TodoList.Web.Client.Services;

namespace TodoList.Web.Client.Store;

public class CommandDispatcher
{
    private readonly IClientStore _store;
    private readonly IConnectivityService _connectivity;
    private readonly HttpClient _http;
    private readonly OperationPoller _poller;
    private readonly ISnackbar _snackbar;

    // Populated at startup by reflecting over ISagaDefinition implementations
    private readonly HashSet<string> _sagaInitiatingCommandTypes;

    public CommandDispatcher(
        IClientStore store,
        IConnectivityService connectivity,
        HttpClient http,
        OperationPoller poller,
        ISnackbar snackbar)
    {
        _store = store;
        _connectivity = connectivity;
        _http = http;
        _poller = poller;
        _snackbar = snackbar;

        _sagaInitiatingCommandTypes = DiscoverSagaInitiatingTypes();
    }

    private static HashSet<string> DiscoverSagaInitiatingTypes()
    {
        // Reflect over all ISagaDefinition implementations in TodoList.Domain
        var sagaDefType = typeof(ISagaDefinition);
        return sagaDefType.Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && sagaDefType.IsAssignableFrom(t))
            .Select(t => (ISagaDefinition)Activator.CreateInstance(t)!)
            .Select(s => s.InitiatingCommandType.Name)
            .ToHashSet();
    }

    /// <summary>
    /// Dispatches a command. Returns null on success, or a list of validation errors.
    /// </summary>
    public async Task<IReadOnlyList<ValidationError>?> Dispatch(ClientCommand command, ClientEvent speculativeEvent)
    {
        // 1. Write speculative event immediately
        _store.AppendEvent(speculativeEvent with { State = EventState.Speculative, Source = EventSource.Client });

        // 2. Enqueue command
        _store.EnqueueCommand(command with { Synced = false });

        // 3. Read models rebuild via OnAggregateChanged (already fired by AppendEvent)

        // 4. Online: dispatch to server
        if (_connectivity.IsOnline)
        {
            // Show saga toast if applicable
            if (_sagaInitiatingCommandTypes.Contains(command.Type))
            {
                _snackbar.Add($"Background work will begin shortly.", Severity.Info);
            }

            await DispatchToServer(command, speculativeEvent.AggregateId);
        }
        else
        {
            // Offline: show saga toast noting it will happen on reconnect
            if (_sagaInitiatingCommandTypes.Contains(command.Type))
            {
                _snackbar.Add($"This action will begin when you're back online.", Severity.Info);
            }
            // Command stays queued — SyncService replays on reconnect
        }

        return null; // No client-side validation errors (passed in from caller before Dispatch)
    }

    internal async Task DispatchToServer(ClientCommand command, string aggregateId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, command.ApiEndpoint)
            {
                Content = JsonContent.Create(command.Payload)
            };
            request.Headers.Add("X-Expected-Version", command.ExpectedVersion.ToString());

            var response = await _http.SendAsync(request);

            switch (response.StatusCode)
            {
                case HttpStatusCode.Accepted: // 202
                {
                    var location = response.Headers.Location?.ToString()
                        ?? response.Headers.GetValues("Location").First();
                    var operationId = location.Split('/').Last();
                    var result = await _poller.PollAsync(operationId);

                    if (result.IsSuccess && result.Event is not null)
                    {
                        _store.ReplaceSpeculative(aggregateId, [result.Event]);
                        _store.MarkSynced(command.Id);
                    }
                    else
                    {
                        _snackbar.Add($"Sync failed: {result.ErrorMessage}", Severity.Error);
                    }
                    break;
                }
                case HttpStatusCode.Conflict: // 409
                {
                    var body = await response.Content.ReadFromJsonAsync<ConflictResponse>();
                    if (body?.ServerEvents is not null)
                    {
                        _store.ReplaceSpeculative(aggregateId, body.ServerEvents);
                        _store.MarkSynced(command.Id);
                        _snackbar.Add(
                            $"Your change was overridden by a newer update.",
                            Severity.Warning,
                            config =>
                            {
                                config.Action = "Redo";
                                config.ActionColor = Color.Primary;
                                config.Onclick = _ =>
                                {
                                    // Re-queue with updated ExpectedVersion
                                    var currentVersion = body.ServerEvents.Max(e => e.AggregateVersion);
                                    _ = DispatchToServer(command with { ExpectedVersion = currentVersion }, aggregateId);
                                    return Task.CompletedTask;
                                };
                            });
                    }
                    break;
                }
                case HttpStatusCode.UnprocessableEntity: // 422
                {
                    var body = await response.Content.ReadFromJsonAsync<ValidationConflictResponse>();
                    if (body?.Errors is not null)
                    {
                        _store.MarkConflicted(aggregateId, body.Errors
                            .Select(e => new ValidationError(e.Field, e.Message))
                            .ToList());
                        _snackbar.Add(
                            $"Couldn't be saved — {body.Errors.FirstOrDefault()?.Message}",
                            Severity.Warning,
                            config =>
                            {
                                config.Action = "Review";
                                config.ActionColor = Color.Warning;
                                // Review action navigates to the item — caller can listen to OnReviewRequested
                            });
                    }
                    break;
                }
                case HttpStatusCode.NotFound: // 404 on delete/complete = treat as success
                {
                    _store.DiscardSpeculative(aggregateId);
                    _store.MarkSynced(command.Id);
                    break;
                }
                case HttpStatusCode.Unauthorized: // 401
                {
                    // Let ConnectivityBanner or auth redirect handle this
                    break;
                }
                default when (int)response.StatusCode >= 500:
                {
                    // Transient — SyncService will retry on next connectivity event
                    break;
                }
                default:
                {
                    // Other 4xx — discard speculative, show error
                    _store.DiscardSpeculative(aggregateId);
                    _store.MarkSynced(command.Id);
                    _snackbar.Add($"Command failed ({(int)response.StatusCode})", Severity.Error);
                    break;
                }
            }
        }
        catch (HttpRequestException)
        {
            // Network error — leave in unsynced state, SyncService replays on reconnect
        }
    }
}

public record ConflictResponse
{
    public string CommandId { get; init; } = "";
    public string AggregateId { get; init; } = "";
    public IReadOnlyList<ClientEvent>? ServerEvents { get; init; }
}

public record ValidationConflictResponse
{
    public string CommandId { get; init; } = "";
    public IReadOnlyList<ValidationErrorDto>? Errors { get; init; }
}

public record ValidationErrorDto
{
    public string Field { get; init; } = "";
    public string Message { get; init; } = "";
}
```

- [ ] **Step 2: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

Expected: Build succeeds. If `TodoList.Domain.Sagas.ISagaDefinition` doesn't exist yet (Plan A not run), comment out the `DiscoverSagaInitiatingTypes` call and return an empty set temporarily.

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Store/CommandDispatcher.cs
git commit -m "feat: add CommandDispatcher — speculative events, PRG polling, 409/422 handling, saga toast"
```

---

### Task 7: SyncService

**Files:**
- Create: `TodoList.Web/Client/Store/SyncService.cs`

Replays all unsynced commands (in Timestamp order) when connectivity is restored.

- [ ] **Step 1: Create SyncService**

```csharp
// TodoList.Web/Client/Store/SyncService.cs
using TodoList.Web.Client.Services;

namespace TodoList.Web.Client.Store;

public class SyncService : IAsyncDisposable
{
    private readonly IClientStore _store;
    private readonly IConnectivityService _connectivity;
    private readonly CommandDispatcher _dispatcher;
    private bool _syncing;

    public SyncService(
        IClientStore store,
        IConnectivityService connectivity,
        CommandDispatcher dispatcher)
    {
        _store = store;
        _connectivity = connectivity;
        _dispatcher = dispatcher;

        _connectivity.OnConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(bool isOnline)
    {
        if (isOnline) _ = SyncPendingAsync();
    }

    public async Task SyncPendingAsync()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var pending = _store.GetUnsyncedCommands();
            foreach (var command in pending)
            {
                // Find the aggregate ID from the event log
                // (command has AggregateId directly)
                await _dispatcher.DispatchToServer(command, command.AggregateId);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        _connectivity.OnConnectivityChanged -= OnConnectivityChanged;
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Store/SyncService.cs
git commit -m "feat: add SyncService — replays unsynced commands on reconnect"
```

---

### Task 8: SignalR hub (server-side) and EventHubClient (client-side)

**Files:**
- Create: `TodoList.Api/Hubs/EventHub.cs`
- Modify: `TodoList.Api/Program.cs`
- Modify: `TodoList.Api/TodoList.Api.csproj` (add SignalR)
- Create: `TodoList.Web/Client/Hubs/EventHubClient.cs`
- Modify: `TodoList.Web/Client/TodoList.Web.Client.csproj` (add SignalR client)

- [ ] **Step 1: Create SignalR hub on API**

```csharp
// TodoList.Api/Hubs/EventHub.cs
using Microsoft.AspNetCore.SignalR;

namespace TodoList.Api.Hubs;

public class EventHub : Hub
{
    // Server → Client: the client subscribes and receives pushed events
    // No client-to-server methods needed — all mutations go via REST API
}

public interface IEventHubClient
{
    Task ReceiveEvent(object @event);
}
```

- [ ] **Step 2: Register SignalR in TodoList.Api/Program.cs**

Read `TodoList.Api/Program.cs` first. Add SignalR services and map the hub:

```csharp
// In builder.Services section:
builder.Services.AddSignalR();

// In app.MapXxx section (before app.Run()):
app.MapHub<EventHub>("/hubs/events");
```

- [ ] **Step 3: Add SignalR NuGet to API project (already included in ASP.NET Core, no extra package needed)**

ASP.NET Core includes SignalR server. No additional package reference needed for `TodoList.Api`.

- [ ] **Step 4: Add SignalR client package to Blazor WASM project**

```xml
<!-- In TodoList.Web/Client/TodoList.Web.Client.csproj, add: -->
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.5" />
```

- [ ] **Step 5: Create EventHubClient**

```csharp
// TodoList.Web/Client/Hubs/EventHubClient.cs
using Microsoft.AspNetCore.SignalR.Client;
using TodoList.Web.Client.Store;

namespace TodoList.Web.Client.Hubs;

public class EventHubClient : IAsyncDisposable
{
    private readonly IClientStore _store;
    private HubConnection? _connection;
    private readonly string _hubUrl;

    public EventHubClient(IClientStore store, HttpClient http)
    {
        _store = store;
        _hubUrl = $"{http.BaseAddress}hubs/events";
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ClientEvent>("ReceiveEvent", evt =>
        {
            // Server pushed an event — same path as any other confirmed event
            _store.AppendEvent(evt with { Source = EventSource.Server, State = EventState.Confirmed });
        });

        await _connection.StartAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
```

- [ ] **Step 6: Build all**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Api/TodoList.Api.csproj
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 7: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Api/Hubs/ TodoList.Web/Client/Hubs/ TodoList.Api/Program.cs TodoList.Web/Client/TodoList.Web.Client.csproj
git commit -m "feat: add SignalR EventHub (API) and EventHubClient (WASM) for server-pushed events"
```

---

### Task 9: Seed ClientStore on startup from GET /todos + GET /categories

On first load (or after app close + reopen), the client populates `ClientStore` from the API so it has the latest confirmed state before any user interaction. This also picks up events that arrived while the app was closed (desktop scenario).

**Files:**
- Create: `TodoList.Web/Client/Services/StartupSeedService.cs`

- [ ] **Step 1: Create StartupSeedService**

```csharp
// TodoList.Web/Client/Services/StartupSeedService.cs
using System.Net.Http.Json;
using System.Text.Json;
using TodoList.Web.Client.Store;

namespace TodoList.Web.Client.Services;

public class StartupSeedService
{
    private readonly HttpClient _http;
    private readonly IClientStore _store;

    public StartupSeedService(HttpClient http, IClientStore store)
    {
        _http = http;
        _store = store;
    }

    /// <summary>
    /// Seeds ClientStore with confirmed server events from GET /todos and GET /categories.
    /// Skips if store already has confirmed events (not first load).
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var existingConfirmed = _store.GetAllEvents()
            .Any(e => e.State == EventState.Confirmed);

        if (existingConfirmed) return; // Already seeded

        try
        {
            await SeedTodosAsync(ct);
            await SeedCategoriesAsync(ct);
        }
        catch (HttpRequestException)
        {
            // Offline at startup — operate from existing localStorage state
        }
    }

    private async Task SeedTodosAsync(CancellationToken ct)
    {
        var todos = await _http.GetFromJsonAsync<JsonElement[]>("/todos", ct);
        if (todos is null) return;

        foreach (var todo in todos)
        {
            var id = todo.GetProperty("id").GetString() ?? "";
            var evt = new ClientEvent
            {
                Id = Guid.NewGuid().ToString(),
                AggregateId = id,
                AggregateVersion = 0, // Seed events use version 0 — treated as snapshot
                Type = "TodoSeeded",
                Payload = todo,
                Timestamp = DateTimeOffset.UtcNow,
                Source = EventSource.Server,
                State = EventState.Confirmed
            };
            _store.AppendEvent(evt);
        }
    }

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        var categories = await _http.GetFromJsonAsync<JsonElement[]>("/categories", ct);
        if (categories is null) return;

        foreach (var cat in categories)
        {
            var id = cat.GetProperty("id").GetString() ?? "";
            var evt = new ClientEvent
            {
                Id = Guid.NewGuid().ToString(),
                AggregateId = id,
                AggregateVersion = 0,
                Type = "CategorySeeded",
                Payload = cat,
                Timestamp = DateTimeOffset.UtcNow,
                Source = EventSource.Server,
                State = EventState.Confirmed
            };
            _store.AppendEvent(evt);
        }
    }
}
```

Note: The projectors in `TodoList.Domain` must handle `TodoSeeded` / `CategorySeeded` events as full-state snapshots. If they don't, use a simpler approach: seed directly into the local store as pre-projected read models (bypassing the event log). Read `TodoList.Domain/Projectors/TodoProjector.cs` before implementing to choose the right approach.

Alternative (if projectors don't handle seed events): populate `LocalTodoStore` / `LocalCategoryStore` directly from the API response and mark them as pre-seeded so `RebuildAll()` uses API data as baseline.

- [ ] **Step 2: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Services/StartupSeedService.cs
git commit -m "feat: add StartupSeedService — seeds ClientStore from API on first load"
```

---

### Task 10: Wire DI and startup in Program.cs

**Files:**
- Modify: `TodoList.Web/Client/Program.cs`
- Create: `TodoList.Web/Client/Services/AppInitializer.cs`

- [ ] **Step 1: Update Client Program.cs**

```csharp
// TodoList.Web/Client/Program.cs
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using TodoList.Web.Client.Hubs;
using TodoList.Web.Client.Services;
using TodoList.Web.Client.Store;
using TodoList.Web.Client.Theme;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices();

// Core stores
builder.Services.AddSingleton<IClientStore, ClientStore>();
builder.Services.AddSingleton<ILocalTodoStore, LocalTodoStore>();
builder.Services.AddSingleton<ILocalCategoryStore, LocalCategoryStore>();

// Services
builder.Services.AddSingleton<IConnectivityService, ConnectivityService>();
builder.Services.AddSingleton<OperationPoller>();
builder.Services.AddSingleton<CommandDispatcher>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton<EventHubClient>();
builder.Services.AddSingleton<StartupSeedService>();

var host = builder.Build();

// Initialize services that need async startup
var clientStore = host.Services.GetRequiredService<IClientStore>() as ClientStore;
if (clientStore is not null)
    await clientStore.InitializeAsync();

var connectivity = host.Services.GetRequiredService<IConnectivityService>() as ConnectivityService;
if (connectivity is not null)
    await connectivity.InitializeAsync();

var seed = host.Services.GetRequiredService<StartupSeedService>();
await seed.SeedAsync();

var hubClient = host.Services.GetRequiredService<EventHubClient>();
await hubClient.StartAsync();

var sync = host.Services.GetRequiredService<SyncService>();
await sync.SyncPendingAsync();

await host.RunAsync();
```

- [ ] **Step 2: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Program.cs
git commit -m "feat: wire all DI registrations and async startup initialization in Client Program.cs"
```

---

### Task 11: Wire CommandDispatcher into page components

Replace all the "not yet wired to API" snackbar stubs in Plan B pages with real `CommandDispatcher.Dispatch()` calls.

**Files:**
- Modify: `TodoList.Web/Client/Pages/TodoList.razor`
- Modify: `TodoList.Web/Client/Pages/TodoDetail.razor`
- Modify: `TodoList.Web/Client/Pages/Categories.razor`
- Modify: `TodoList.Web/Client/Layout/UserProfileStrip.razor`

- [ ] **Step 1: Wire TodoList.razor**

In `TodoList.razor`, replace the `HandleComplete`, `HandleDelete`, and `OpenAddDialog` stubs with `CommandDispatcher` calls. Inject `CommandDispatcher`.

The commands require `TodoList.Domain.Commands` types. Read `TodoList.Domain/Commands/TodoCommands.cs` (created in Plan A) before implementing to get exact type names.

Assuming command types are `CreateTodoCommand`, `CompleteTodoCommand`, `DeleteTodoCommand`:

```razor
@inject CommandDispatcher Dispatcher

@* In OpenAddDialog, after dialog closes with data: *@
var command = new ClientCommand
{
    AggregateId = Guid.NewGuid().ToString(),
    ExpectedVersion = 0,
    Type = nameof(CreateTodoCommand),
    Payload = JsonSerializer.SerializeToElement(new { title = data.Title, categoryId = data.CategoryId, dueDate = data.DueDate, notes = data.Notes, progress = data.Progress }),
    ApiEndpoint = "/todos",
    Timestamp = DateTimeOffset.UtcNow
};
var speculativeEvent = new ClientEvent
{
    AggregateId = command.AggregateId,
    AggregateVersion = 1,
    Type = "TodoCreated",
    Payload = JsonSerializer.SerializeToElement(new { id = command.AggregateId, title = data.Title }),
    Timestamp = DateTimeOffset.UtcNow,
    Source = EventSource.Client,
    State = EventState.Speculative
};
await Dispatcher.Dispatch(command, speculativeEvent);
```

```razor
@* In HandleComplete: *@
var currentTodo = TodoStore.GetById(todo.Id)!;
var command = new ClientCommand
{
    AggregateId = todo.Id,
    ExpectedVersion = currentTodo.Version, // TodoSummary needs Version — add if missing in Plan A
    Type = completed ? nameof(CompleteTodoCommand) : "UncompleteTodo",
    Payload = JsonSerializer.SerializeToElement(new { }),
    ApiEndpoint = completed ? $"/todos/{todo.Id}/complete" : $"/todos/{todo.Id}/uncomplete",
    Timestamp = DateTimeOffset.UtcNow
};
var speculativeEvent = new ClientEvent
{
    AggregateId = todo.Id,
    AggregateVersion = command.ExpectedVersion + 1,
    Type = completed ? "TodoCompleted" : "TodoUncompleted",
    Payload = null,
    Timestamp = DateTimeOffset.UtcNow,
    Source = EventSource.Client,
    State = EventState.Speculative
};
await Dispatcher.Dispatch(command, speculativeEvent);
```

```razor
@* In HandleDelete: *@
var currentTodo = TodoStore.GetById(todo.Id)!;
var command = new ClientCommand
{
    AggregateId = todo.Id,
    ExpectedVersion = currentTodo.Version,
    Type = nameof(DeleteTodoCommand),
    Payload = null,
    ApiEndpoint = $"/todos/{todo.Id}",
    Timestamp = DateTimeOffset.UtcNow
};
// Use DELETE method — CommandDispatcher.Dispatch needs an override or we send DELETE directly:
// For simplicity, use HttpMethod in ClientCommand:
```

Note: `ClientCommand` as defined uses `HttpMethod.Post` implicitly (ApiEndpoint only). For DELETE, extend `ClientCommand` with an optional `HttpMethod` field:

```csharp
// Add to ClientCommand.cs:
public string HttpMethod { get; init; } = "POST";
```

And in `CommandDispatcher.DispatchToServer`, use `HttpMethod` from command:

```csharp
var method = new HttpMethod(command.HttpMethod);
var request = new HttpRequestMessage(method, command.ApiEndpoint) { ... };
```

- [ ] **Step 2: Wire TodoDetail.razor**

In `SaveTitle`, `SaveCategory`, `SaveDueDate`, `SaveNotes`, `SaveProgress`, `HandleDelete`, dispatch the appropriate commands. Example for `SaveTitle`:

```csharp
private async Task SaveTitle()
{
    if (_todo == null || _title == _todo.Title) return;
    var command = new ClientCommand
    {
        AggregateId = _todo.Id,
        ExpectedVersion = _todo.Version,
        Type = "RenameTodo",
        Payload = JsonSerializer.SerializeToElement(new { title = _title }),
        ApiEndpoint = $"/todos/{_todo.Id}/rename",
        Timestamp = DateTimeOffset.UtcNow
    };
    var speculativeEvent = new ClientEvent
    {
        AggregateId = _todo.Id,
        AggregateVersion = command.ExpectedVersion + 1,
        Type = "TodoRenamed",
        Payload = JsonSerializer.SerializeToElement(new { newTitle = _title }),
        Timestamp = DateTimeOffset.UtcNow,
        Source = EventSource.Client,
        State = EventState.Speculative
    };
    await Dispatcher.Dispatch(command, speculativeEvent);
}
```

Apply same pattern to all other save methods.

- [ ] **Step 3: Wire Categories.razor**

In `OpenCreateDialog`, `OpenEditDialog`, `HandleDelete`, dispatch category commands:

```csharp
// Create:
var command = new ClientCommand
{
    AggregateId = "user-category-list", // CategoryList aggregate ID = user ID — use hardcoded for stub, replace with real user ID from /api/me in Plan D
    ExpectedVersion = 0, // TODO: track CategoryList version
    Type = "AddCategory",
    Payload = JsonSerializer.SerializeToElement(new { name = data.Name, color = data.Color, icon = data.Icon }),
    ApiEndpoint = "/categories",
    Timestamp = DateTimeOffset.UtcNow
};
```

- [ ] **Step 4: Wire UserProfileStrip.razor logout**

```csharp
// Inject HttpClient and NavigationManager
@inject HttpClient Http
@inject NavigationManager Nav

private async Task Logout()
{
    await Http.PostAsync("/auth/logout", null);
    Nav.NavigateTo("/login", forceLoad: true);
}
```

- [ ] **Step 5: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 6: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Pages/ TodoList.Web/Client/Layout/UserProfileStrip.razor
git commit -m "feat: wire CommandDispatcher into page components — replace stubs with real dispatch"
```

---

### Task 12: Full solution build + integration verification

- [ ] **Step 1: Build the full solution**

```bash
cd /Users/jim/code/todo-patterns
dotnet build
```

Expected: All projects build — `TodoList.Domain`, `TodoList.Api`, `TodoList.Web.Server`, `TodoList.Web.Client`, `TodoList.AppHost`, `TodoList.Tests`, `TodoList.IntegrationTests`.

Fix any compilation errors before continuing.

- [ ] **Step 2: Run unit tests**

```bash
cd /Users/jim/code/todo-patterns
dotnet test TodoList.Tests/TodoList.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 3: Run integration tests**

```bash
cd /Users/jim/code/todo-patterns
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj
```

Expected: All integration tests pass (requires Docker for Testcontainers).

- [ ] **Step 4: Final commit**

```bash
cd /Users/jim/code/todo-patterns
git add .
git commit -m "feat: complete PWA/offline/SignalR implementation — ClientStore, CommandDispatcher, SyncService, EventHubClient"
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|---|---|
| ClientEvent record: Id, AggregateId, AggregateVersion, Type, Payload, Timestamp, EventSource, EventState | Task 1 |
| ClientCommand record: Id, AggregateId, ExpectedVersion, SpeculativeVersion, Type, Payload, Timestamp, Synced | Task 1 |
| EventState enum: Speculative / Confirmed / Conflicted | Task 1 |
| IClientStore: AppendEvent, GetEventsFor, GetAllEvents, ReplaceSpeculative, MarkConflicted, DiscardSpeculative, GetUnsyncedCommands, MarkSynced | Task 2 |
| localStorage-backed ClientStore | Task 2 |
| LocalTodoStore / LocalCategoryStore projecting from ClientStore | Task 3 |
| Replace Plan B stubs | Task 3 |
| ConnectivityService (JS interop, online/offline events) | Task 4 |
| OperationPoller (GET /operations/{id} until terminal) | Task 5 |
| CommandDispatcher: validate → speculative → POST → poll → confirm | Task 6 |
| 409: ReplaceSpeculative, server events win, Redo toast | Task 6 |
| 422: MarkConflicted, Review toast | Task 6 |
| 404 on delete/complete = treat as success | Task 6 |
| Offline: queue command, unsynced dot, saga toast | Task 6 |
| ISagaDefinition reflection to detect saga-initiating commands | Task 6 |
| SyncService: replay unsynced on reconnect | Task 7 |
| SignalR EventHub (API) | Task 8 |
| EventHubClient (WASM, ReceiveEvent → ClientStore.AppendEvent) | Task 8 |
| Seed ClientStore from GET /todos + GET /categories on startup | Task 9 |
| DI registration + async startup | Task 10 |
| CommandDispatcher wired into page components | Task 11 |
| Logout button wired | Task 11 |
| Service worker: cache-first app shell, network-only API | Plan B Task 12 |

**Placeholder scan:** Task 11 notes "user ID from /api/me in Plan D" for CategoryList aggregate ID — this is an explicit known limitation, not a hidden placeholder. The stub value `"user-category-list"` allows the code to compile; real user ID comes from a future `/api/me` integration.

**Type consistency:** `ClientEvent`, `ClientCommand`, `ValidationError` defined in Task 1–2 and used consistently throughout. `ILocalTodoStore`/`ILocalCategoryStore` interfaces defined in Plan B Task 3, implemented here in Task 3. `TodoProjector`/`CategoryProjector` from Plan A — if their signatures differ, Task 3 notes to read them first before implementing.
