// TodoList.Api/Data/CategoryListEntity.cs
namespace TodoList.Api.Data;

public class CategoryListEntity
{
    public string UserId { get; set; } = "";
    public int Version { get; set; }
    public List<CategoryEntity> Categories { get; set; } = [];
}
