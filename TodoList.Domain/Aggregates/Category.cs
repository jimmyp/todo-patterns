// TodoList.Domain/Aggregates/Category.cs
namespace TodoList.Domain.Aggregates;

public record Category(
    Guid Id,
    string Name,
    string Color,
    string Icon,
    int Order,
    DateTimeOffset CreatedAt);
