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
