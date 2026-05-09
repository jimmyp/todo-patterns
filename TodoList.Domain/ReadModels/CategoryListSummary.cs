// TodoList.Domain/ReadModels/CategoryListSummary.cs
namespace TodoList.Domain.ReadModels;

/// <summary>
/// List-level view of the CategoryList aggregate — exposes the aggregate version
/// (needed for ExpectedVersion on mutating commands) alongside the projected categories.
/// </summary>
public record CategoryListSummary
{
    public int Version { get; init; }
    public IReadOnlyList<CategorySummary> Categories { get; init; } = [];
}
