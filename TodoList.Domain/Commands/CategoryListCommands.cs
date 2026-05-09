// TodoList.Domain/Commands/CategoryListCommands.cs
namespace TodoList.Domain.Commands;

public record AddCategoryCommand(string Name, string Color, string Icon, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record RenameCategoryCommand(Guid CategoryId, string NewName, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record ChangeCategoryColorCommand(Guid CategoryId, string Color, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record ChangeCategoryIconCommand(Guid CategoryId, string Icon, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record ReorderCategoryCommand(Guid CategoryId, int Order, string UserId, Guid OperationId, int ExpectedVersion = 0);
public record RemoveCategoryCommand(Guid CategoryId, string UserId, Guid OperationId, int ExpectedVersion = 0);
