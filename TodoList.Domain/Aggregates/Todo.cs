// TodoList.Domain/Aggregates/Todo.cs
namespace TodoList.Domain.Aggregates;

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
    public Guid? CategoryId { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public string? Notes { get; private set; }
    public int Progress { get; private set; }

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
            (todo, [new TodoCreatedEvent(todo.Id, todo.Title, now)]));
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Complete(DateTimeOffset now)
    {
        var errors = new List<string>();
        if (IsDeleted)   errors.Add("Cannot complete a deleted todo");
        if (IsCompleted) errors.Add("Already completed");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        IsCompleted = true;
        CompletedAt = now;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoCompletedEvent(Id, now)]);
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
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoUncompletedEvent(Id)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Already deleted");

        IsDeleted = true;
        DeletedAt = now;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoDeletedEvent(Id, now)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Rename(string newTitle)
    {
        var errors = new List<string>();
        if (IsDeleted) errors.Add("Cannot rename a deleted todo");
        if (string.IsNullOrWhiteSpace(newTitle)) errors.Add("Title cannot be empty");
        if (newTitle?.Length > 500) errors.Add("Title cannot exceed 500 characters");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        Title = newTitle!.Trim();
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoRenamedEvent(Id, Title)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> AssignCategory(Guid categoryId)
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Cannot update a deleted todo");

        CategoryId = categoryId;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoCategoryAssignedEvent(Id, categoryId)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> UnassignCategory()
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Cannot update a deleted todo");

        CategoryId = null;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoCategoryUnassignedEvent(Id)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> SetDueDate(DateTimeOffset dueDate)
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Cannot update a deleted todo");

        DueDate = dueDate;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoDueDateSetEvent(Id, dueDate)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> ClearDueDate()
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Cannot update a deleted todo");

        DueDate = null;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoDueDateClearedEvent(Id)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> UpdateNotes(string? notes)
    {
        var errors = new List<string>();
        if (IsDeleted) errors.Add("Cannot update a deleted todo");
        if (notes?.Length > 2000) errors.Add("Notes cannot exceed 2000 characters");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        Notes = notes;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoNotesUpdatedEvent(Id, notes)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> UpdateProgress(int progress)
    {
        var errors = new List<string>();
        if (IsDeleted) errors.Add("Cannot update a deleted todo");
        if (progress < 0 || progress > 100) errors.Add("Progress must be between 0 and 100");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        Progress = progress;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoProgressUpdatedEvent(Id, progress)]);
    }
}
