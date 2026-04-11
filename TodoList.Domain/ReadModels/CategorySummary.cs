// TodoList.Domain/ReadModels/CategorySummary.cs
namespace TodoList.Domain.ReadModels;

public record CategorySummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Color { get; init; } = "";
    public string Icon { get; init; } = "";
    public int Order { get; init; }
    public int TodoCount { get; init; }
}
