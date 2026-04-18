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
            CancellationToken ct) =>
        {
            var operation = await ops.GetByIdAsync(id, ct);
            if (operation is null) return Results.NotFound();

            JsonElement? result = operation.ResultJson is not null
                ? JsonSerializer.Deserialize<JsonElement>(operation.ResultJson)
                : null;

            return Results.Ok(new
            {
                id            = operation.Id,
                status        = operation.Status,
                result,
                failureReason = operation.FailureReason,
                isRetryable   = operation.IsRetryable,
                createdAt     = operation.CreatedAt,
                completedAt   = operation.CompletedAt
            });
        }).RequireAuthorization();
    }
}
