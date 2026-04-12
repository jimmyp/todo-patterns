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
