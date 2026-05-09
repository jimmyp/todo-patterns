// TodoList.Api/Data/CategoryEntity.cs
namespace TodoList.Api.Data;

public class CategoryEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Order { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
