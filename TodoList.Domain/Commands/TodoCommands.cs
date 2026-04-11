// TodoList.Domain/Commands/TodoCommands.cs
namespace TodoList.Domain.Commands;

public record CreateTodoCommand(string Title, Guid? CategoryId = null, DateTimeOffset? DueDate = null, string? Notes = null, int Progress = 0);
public record RenameTodoCommand(Guid TodoId, string NewTitle);
public record CompleteTodoCommand(Guid TodoId);
public record UncompleteTodoCommand(Guid TodoId);
public record DeleteTodoCommand(Guid TodoId);
public record AssignCategoryCommand(Guid TodoId, Guid CategoryId);
public record UnassignCategoryCommand(Guid TodoId);
public record SetDueDateCommand(Guid TodoId, DateTimeOffset DueDate);
public record ClearDueDateCommand(Guid TodoId);
public record UpdateNotesCommand(Guid TodoId, string? Notes);
public record UpdateProgressCommand(Guid TodoId, int Progress);
