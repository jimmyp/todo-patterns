// TodoList.Api/Data/Projections/TodoSummaryEntity.cs
namespace TodoList.Api.Data.Projections;

public class TodoSummaryEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryColor { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public int Progress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Version { get; set; }
}
