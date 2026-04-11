// TodoList.Domain/Commands/CategoryListCommands.cs
namespace TodoList.Domain.Commands;

public record AddCategoryCommand(string Name, string Color, string Icon);
public record RenameCategoryCommand(Guid CategoryId, string NewName);
public record ChangeCategoryColorCommand(Guid CategoryId, string Color);
public record ChangeCategoryIconCommand(Guid CategoryId, string Icon);
public record ReorderCategoryCommand(Guid CategoryId, int Order);
public record RemoveCategoryCommand(Guid CategoryId);
