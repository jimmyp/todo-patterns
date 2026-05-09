// TodoList.Domain/Projectors/DomainEventEnvelope.cs
using System.Text.Json;

namespace TodoList.Domain.Projectors;

/// <summary>
/// A lightweight envelope for passing events to projectors.
/// Used by the client-side store to bridge ClientEvent to the domain projectors.
/// </summary>
public record DomainEventEnvelope(
    string AggregateId,
    int AggregateVersion,
    string Type,
    JsonElement? Payload);
