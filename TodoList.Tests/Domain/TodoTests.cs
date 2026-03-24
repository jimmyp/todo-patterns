namespace TodoList.Tests.Domain;

[Trait("Category", "Unit")]
public class TodoTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_with_valid_title_returns_todo_and_created_event()
    {
        var result = Todo.Create("buy milk", Now);

        result.IsSuccess.Should().BeTrue();
        var (todo, events) = result.Value!;
        todo.Title.Should().Be("buy milk");
        todo.IsCompleted.Should().BeFalse();
        todo.IsDeleted.Should().BeFalse();
        todo.CreatedAt.Should().Be(Now);
        events.Should().ContainSingle().Which.Should().BeOfType<TodoCreatedEvent>();
    }

    [Fact]
    public void Create_trims_title_whitespace()
    {
        var result = Todo.Create("  buy milk  ", Now);
        result.Value!.todo.Title.Should().Be("buy milk");
    }

    [Fact]
    public void Create_with_empty_title_returns_error_without_producing_todo()
    {
        var result = Todo.Create("", Now);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Title cannot be empty");
        result.Value.Should().Be(default);
    }

    [Fact]
    public void Create_with_whitespace_title_returns_error()
    {
        var result = Todo.Create("   ", Now);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Create_with_title_over_500_chars_returns_error()
    {
        var result = Todo.Create(new string('x', 501), Now);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Title cannot exceed 500 characters");
    }

    [Fact]
    public void Create_with_empty_and_too_long_title_returns_both_errors()
    {
        // Edge case: whitespace string longer than 500 chars
        var result = Todo.Create(new string(' ', 501), Now);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
    }

    // ── Complete ─────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_on_open_todo_returns_completed_event()
    {
        var todo = Todo.Create("buy milk", Now).Value!.todo;

        var result = todo.Complete(Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().BeOfType<TodoCompletedEvent>();
        todo.IsCompleted.Should().BeTrue();
        todo.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public void Complete_already_completed_todo_returns_error_without_changing_state()
    {
        var todo = Todo.Create("buy milk", Now).Value!.todo;
        todo.Complete(Now);

        var result = todo.Complete(Now);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Already completed");
        todo.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Complete_deleted_todo_returns_error()
    {
        var todo = Todo.Create("buy milk", Now).Value!.todo;
        todo.Delete(Now);

        var result = todo.Complete(Now);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Cannot complete a deleted todo");
    }

    // ── Uncomplete ───────────────────────────────────────────────────────────

    [Fact]
    public void Uncomplete_after_complete_returns_uncompleted_event()
    {
        var todo = Todo.Create("buy milk", Now).Value!.todo;
        todo.Complete(Now);

        var result = todo.Uncomplete();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().BeOfType<TodoUncompletedEvent>();
        todo.IsCompleted.Should().BeFalse();
        todo.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Uncomplete_open_todo_returns_error_without_changing_state()
    {
        var todo = Todo.Create("buy milk", Now).Value!.todo;

        var result = todo.Uncomplete();

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Not completed");
    }

    [Fact]
    public void Uncomplete_deleted_todo_returns_error()
    {
        var todo = Todo.Create("buy milk", Now).Value!.todo;
        todo.Complete(Now);
        todo.Delete(Now);

        var result = todo.Uncomplete();

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Cannot uncomplete a deleted todo");
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_open_todo_returns_deleted_event()
    {
        var todo = Todo.Create("buy milk", Now).Value!.todo;

        var result = todo.Delete(Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().BeOfType<TodoDeletedEvent>();
        todo.IsDeleted.Should().BeTrue();
        todo.DeletedAt.Should().Be(Now);
    }

    [Fact]
    public void Delete_completed_todo_returns_deleted_event()
    {
        var todo = Todo.Create("buy milk", Now).Value!.todo;
        todo.Complete(Now);

        var result = todo.Delete(Now);

        result.IsSuccess.Should().BeTrue();
        todo.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Delete_already_deleted_todo_returns_error_without_changing_state()
    {
        var todo = Todo.Create("buy milk", Now).Value!.todo;
        todo.Delete(Now);

        var result = todo.Delete(Now);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Already deleted");
    }
}
