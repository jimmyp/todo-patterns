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
