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
