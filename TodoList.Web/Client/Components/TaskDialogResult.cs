namespace TodoList.Web.Client.Components;

public record TaskDialogResult
{
    public string Title { get; init; } = "";
    public string? CategoryId { get; init; }
    public DateTimeOffset? DueDate { get; init; }
    public string? Notes { get; init; }
    public int Progress { get; init; }
}
