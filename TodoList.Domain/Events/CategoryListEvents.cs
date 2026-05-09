// TodoList.Domain/Events/CategoryListEvents.cs
namespace TodoList.Domain.Events;

public record CategoryAddedEvent(string UserId, Guid CategoryId, string Name, string Color, string Icon, int Order) : IDomainEvent;
public record CategoryRenamedEvent(string UserId, Guid CategoryId, string NewName) : IDomainEvent;
public record CategoryColorChangedEvent(string UserId, Guid CategoryId, string NewColor) : IDomainEvent;
public record CategoryIconChangedEvent(string UserId, Guid CategoryId, string NewIcon) : IDomainEvent;
public record CategoryReorderedEvent(string UserId, Guid CategoryId, int NewOrder) : IDomainEvent;
public record CategoryRemovedEvent(string UserId, Guid CategoryId) : IDomainEvent;
