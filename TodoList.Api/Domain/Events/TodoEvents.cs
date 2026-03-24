namespace TodoList.Api.Domain.Events;

public record TodoCreatedEvent(
    Guid TodoId,
    string Title,
    DateTimeOffset CreatedAt) : IDomainEvent;

public record TodoCompletedEvent(
    Guid TodoId,
    DateTimeOffset CompletedAt) : IDomainEvent;

public record TodoUncompletedEvent(
    Guid TodoId) : IDomainEvent;

public record TodoDeletedEvent(
    Guid TodoId,
    DateTimeOffset DeletedAt) : IDomainEvent;
