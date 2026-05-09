using System.Security.Claims;
using System.Text.Json;
using TodoList.Api.Data;

namespace TodoList.Api.Endpoints;

public static class OperationEndpoints
{
    public static void MapOperationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/todos/operations/{id:guid}", async (
            Guid id,
            IOperationRepository ops,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var operation = await ops.GetByIdAsync(id, ct);
            // 404 (not 403) when the caller doesn't own the operation — operation IDs
            // are not a side channel for cross-user existence checks.
            var callerId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            if (operation is null || operation.UserId != callerId) return Results.NotFound();

            JsonElement? result = operation.ResultJson is not null
                ? JsonSerializer.Deserialize<JsonElement>(operation.ResultJson)
                : null;

            return Results.Ok(new
            {
                id            = operation.Id,
                status        = operation.Status,
                result,
                failureReason = operation.FailureReason,
                failureCode   = operation.FailureCode,
                isRetryable   = operation.IsRetryable,
                createdAt     = operation.CreatedAt,
                completedAt   = operation.CompletedAt
            });
        }).RequireAuthorization();
    }
}
