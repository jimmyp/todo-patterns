// TodoList.Domain/Commands/TodoCommands.cs
namespace TodoList.Domain.Commands;

public record CreateTodoCommand(string Title, Guid? CategoryId = null, DateTimeOffset? DueDate = null, string? Notes = null, int Progress = 0, int ExpectedVersion = 0);
public record RenameTodoCommand(Guid TodoId, string NewTitle, int ExpectedVersion = 0);
public record CompleteTodoCommand(Guid TodoId, int ExpectedVersion = 0);
public record UncompleteTodoCommand(Guid TodoId, int ExpectedVersion = 0);
public record DeleteTodoCommand(Guid TodoId, int ExpectedVersion = 0);
public record AssignCategoryCommand(Guid TodoId, Guid CategoryId, int ExpectedVersion = 0);
public record UnassignCategoryCommand(Guid TodoId, int ExpectedVersion = 0);
public record SetDueDateCommand(Guid TodoId, DateTimeOffset DueDate, int ExpectedVersion = 0);
public record ClearDueDateCommand(Guid TodoId, int ExpectedVersion = 0);
public record UpdateNotesCommand(Guid TodoId, string? Notes, int ExpectedVersion = 0);
public record UpdateProgressCommand(Guid TodoId, int Progress, int ExpectedVersion = 0);
