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
            var confirmed = evt with { Source = EventSource.Server, State = EventState.Confirmed };
            // If we have a speculative event for this aggregate, replace it so the
            // optimistic write transitions to the authoritative server version cleanly.
            // Otherwise it's a push for an aggregate we didn't mutate — just append.
            var hasSpeculative = _store.GetEventsFor(confirmed.AggregateId)
                .Any(e => e.State == EventState.Speculative);
            if (hasSpeculative)
                _store.ReplaceSpeculative(confirmed.AggregateId, [confirmed]);
            else
                _store.AppendEvent(confirmed);
        });

        await _connection.StartAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
