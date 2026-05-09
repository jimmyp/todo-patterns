// TodoList.Domain/Events/TodoEvents.cs
using TodoList.Domain.Sagas;
using Wolverine.Persistence.Sagas;

namespace TodoList.Domain.Events;

public record TodoCreatedEvent(Guid TodoId, string Title, DateTimeOffset CreatedAt) : IDomainEvent;
public record TodoCompletedEvent(Guid TodoId, DateTimeOffset CompletedAt) : IDomainEvent;
public record TodoUncompletedEvent(Guid TodoId) : IDomainEvent;
public record TodoDeletedEvent(Guid TodoId, DateTimeOffset DeletedAt) : IDomainEvent;
public record TodoRenamedEvent(Guid TodoId, string NewTitle) : IDomainEvent;
public record TodoCategoryAssignedEvent(Guid TodoId, Guid CategoryId) : IDomainEvent;
public record TodoCategoryUnassignedEvent(Guid TodoId) : IDomainEvent;

// [SagaIdentity] tells Wolverine to use TodoId as the saga state id when routing this
// event to DueReminderSaga (which keys on Guid Id). Without it Wolverine's name
// convention can't bind TodoId -> saga.Id.
[SagaInitiator]
public record TodoDueDateSetEvent([property: SagaIdentity] Guid TodoId, DateTimeOffset DueDate, string? UserId = null) : IDomainEvent;

public record TodoDueDateClearedEvent([property: SagaIdentity] Guid TodoId) : IDomainEvent;
public record TodoNotesUpdatedEvent(Guid TodoId, string? Notes) : IDomainEvent;
public record TodoProgressUpdatedEvent(Guid TodoId, int Progress) : IDomainEvent;
