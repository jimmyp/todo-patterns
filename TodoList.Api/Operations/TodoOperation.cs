namespace TodoList.Api.Operations;

public class TodoOperation
{
    public Guid Id { get; set; }
    /// <summary>
    /// Caller who initiated the operation. The GET /todos/operations/{id} endpoint
    /// 404s when this doesn't match the authenticated user's id, so operation IDs
    /// are not a side channel for cross-user data.
    /// </summary>
    public string UserId { get; set; } = "";
    public string Status { get; set; } = "pending";  // pending | processing | complete | failed
    public string? ResultJson { get; set; }
    public string? FailureReason { get; set; }
    /// <summary>
    /// Machine-readable failure category. Lets the client branch without substring-matching
    /// FailureReason. Known values: "VERSION_CONFLICT", "VALIDATION_ERROR", "NOT_FOUND",
    /// "INTERNAL_ERROR". null when Status != "failed".
    /// </summary>
    public string? FailureCode { get; set; }
    public bool IsRetryable { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public static TodoOperation CreateCompleted(Guid id, string resultJson)
    {
        return new TodoOperation
        {
            Id = id,
            Status = "complete",
            ResultJson = resultJson,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }
}
