// TodoList.Domain/Events/TodoEvents.cs
namespace TodoList.Domain.Events;

public record TodoCreatedEvent(Guid TodoId, string Title, DateTimeOffset CreatedAt) : IDomainEvent;
public record TodoCompletedEvent(Guid TodoId, DateTimeOffset CompletedAt) : IDomainEvent;
public record TodoUncompletedEvent(Guid TodoId) : IDomainEvent;
public record TodoDeletedEvent(Guid TodoId, DateTimeOffset DeletedAt) : IDomainEvent;
public record TodoRenamedEvent(Guid TodoId, string NewTitle) : IDomainEvent;
public record TodoCategoryAssignedEvent(Guid TodoId, Guid CategoryId) : IDomainEvent;
public record TodoCategoryUnassignedEvent(Guid TodoId) : IDomainEvent;
public record TodoDueDateSetEvent(Guid TodoId, DateTimeOffset DueDate) : IDomainEvent;
public record TodoDueDateClearedEvent(Guid TodoId) : IDomainEvent;
public record TodoNotesUpdatedEvent(Guid TodoId, string? Notes) : IDomainEvent;
public record TodoProgressUpdatedEvent(Guid TodoId, int Progress) : IDomainEvent;
