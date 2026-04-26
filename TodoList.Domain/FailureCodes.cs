namespace TodoList.Domain;

/// <summary>
/// Typed failure codes the API returns on operation poll. Lets the client branch
/// without substring-matching FailureReason. See spec §1.
/// </summary>
public static class FailureCodes
{
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string InternalError = "INTERNAL_ERROR";
}
