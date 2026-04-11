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
    public string HttpMethod { get; init; } = "POST";
}
