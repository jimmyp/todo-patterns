// TodoList.Domain/Commands/TodoCommands.cs
namespace TodoList.Domain.Commands;

public record CreateTodoCommand(string Title, string UserId, Guid OperationId, Guid? CategoryId = null, DateTimeOffset? DueDate = null, string? Notes = null, int Progress = 0);
public record RenameTodoCommand(Guid TodoId, string NewTitle, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record CompleteTodoCommand(Guid TodoId, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record UncompleteTodoCommand(Guid TodoId, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record DeleteTodoCommand(Guid TodoId, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record AssignCategoryCommand(Guid TodoId, Guid CategoryId, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record UnassignCategoryCommand(Guid TodoId, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record SetDueDateCommand(Guid TodoId, DateTimeOffset DueDate, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record ClearDueDateCommand(Guid TodoId, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record UpdateNotesCommand(Guid TodoId, string? Notes, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record UpdateProgressCommand(Guid TodoId, int Progress, string UserId, Guid OperationId, int ExpectedVersion = 0);
