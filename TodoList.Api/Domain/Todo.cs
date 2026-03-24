namespace TodoList.Api.Domain;

public class Todo
{
    private Todo() { }  // EF Core requires parameterless constructor

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public bool IsCompleted { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public static DomainResult<(Todo todo, IReadOnlyList<IDomainEvent> events)> Create(
        string title, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(title)) errors.Add("Title cannot be empty");
        if (title?.Length > 500)             errors.Add("Title cannot exceed 500 characters");
        if (errors.Count > 0)
            return DomainResult<(Todo, IReadOnlyList<IDomainEvent>)>.Fail([..errors]);

        var todo = new Todo
        {
            Id        = Guid.NewGuid(),
            Title     = title!.Trim(),
            CreatedAt = now
        };

        return DomainResult<(Todo, IReadOnlyList<IDomainEvent>)>.Ok(
            (todo, [new Events.TodoCreatedEvent(todo.Id, todo.Title, now)]));
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Complete(DateTimeOffset now)
    {
        var errors = new List<string>();
        if (IsDeleted)    errors.Add("Cannot complete a deleted todo");
        if (IsCompleted)  errors.Add("Already completed");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        IsCompleted = true;
        CompletedAt = now;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok(
            [new Events.TodoCompletedEvent(Id, now)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Uncomplete()
    {
        var errors = new List<string>();
        if (IsDeleted)    errors.Add("Cannot uncomplete a deleted todo");
        if (!IsCompleted) errors.Add("Not completed");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        IsCompleted = false;
        CompletedAt = null;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok(
            [new Events.TodoUncompletedEvent(Id)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Already deleted");

        IsDeleted = true;
        DeletedAt = now;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok(
            [new Events.TodoDeletedEvent(Id, now)]);
    }
}
