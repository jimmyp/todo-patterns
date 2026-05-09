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
