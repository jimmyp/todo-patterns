// TodoList.Domain/ReadModels/TodoSummary.cs
namespace TodoList.Domain.ReadModels;

public record TodoSummary
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public bool IsCompleted { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public string? CategoryColor { get; init; }
    public DateTimeOffset? DueDate { get; init; }
    public bool IsOverdue => DueDate.HasValue && !IsCompleted && DueDate.Value < DateTimeOffset.UtcNow;
    public int Progress { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int Version { get; init; }
}
