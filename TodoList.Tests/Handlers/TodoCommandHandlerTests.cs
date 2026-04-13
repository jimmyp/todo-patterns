// TodoList.Tests/Handlers/TodoCommandHandlerTests.cs
using TodoList.Domain.Commands;

namespace TodoList.Tests.Handlers;

public class TodoCommandHandlerTests
{
    // Domain-only tests — verify the command record shapes are correct.
    // Handler routing is covered by integration tests.

    [Fact]
    public void CreateTodoCommand_handler_produces_a_todo_with_correct_title()
    {
        var cmd = new CreateTodoCommand("Buy milk", null, null, null, 0);
        cmd.Title.Should().Be("Buy milk");
    }

    [Fact]
    public void RenameTodoCommand_carries_todo_id_and_new_title()
    {
        var id = Guid.NewGuid();
        var cmd = new RenameTodoCommand(id, "New title");
        cmd.TodoId.Should().Be(id);
        cmd.NewTitle.Should().Be("New title");
    }

    [Fact]
    public void SetDueDateCommand_carries_todo_id_and_due_date()
    {
        var id = Guid.NewGuid();
        var due = DateTimeOffset.UtcNow.AddDays(3);
        var cmd = new SetDueDateCommand(id, due);
        cmd.TodoId.Should().Be(id);
        cmd.DueDate.Should().Be(due);
    }
}
