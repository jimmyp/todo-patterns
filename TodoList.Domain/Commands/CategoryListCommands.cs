// TodoList.Domain/Commands/CategoryListCommands.cs
namespace TodoList.Domain.Commands;

public record AddCategoryCommand(string Name, string Color, string Icon, int ExpectedVersion = 0);
public record RenameCategoryCommand(Guid CategoryId, string NewName, int ExpectedVersion = 0);
public record ChangeCategoryColorCommand(Guid CategoryId, string Color, int ExpectedVersion = 0);
public record ChangeCategoryIconCommand(Guid CategoryId, string Icon, int ExpectedVersion = 0);
public record ReorderCategoryCommand(Guid CategoryId, int Order, int ExpectedVersion = 0);
public record RemoveCategoryCommand(Guid CategoryId, int ExpectedVersion = 0);
