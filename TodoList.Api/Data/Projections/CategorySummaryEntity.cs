// TodoList.Api/Data/Projections/CategorySummaryEntity.cs
namespace TodoList.Api.Data.Projections;

public class CategorySummaryEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Order { get; set; }
    public int TodoCount { get; set; }
    public int Version { get; set; }
}
