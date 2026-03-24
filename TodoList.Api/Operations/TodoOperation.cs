namespace TodoList.Api.Operations;

public class TodoOperation
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "pending";  // pending | processing | complete | failed
    public string? ResultJson { get; set; }
    public string? FailureReason { get; set; }
    public bool IsRetryable { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
