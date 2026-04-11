using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TodoList.Api.Data;
using TodoList.Domain.Aggregates;
using TodoList.Api.Operations;

namespace TodoList.Api.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/todos");

        group.MapGet("/", async (ITodoRepository repo, CancellationToken ct) =>
        {
            var todos = await repo.GetAllAsync(ct);
            return Results.Ok(todos.ConvertAll(t => new TodoResponse(t)));
        });

        group.MapGet("/{id:guid}", async (Guid id, ITodoRepository repo, CancellationToken ct) =>
        {
            var todo = await repo.GetByIdAsync(id, ct);
            return todo is null ? Results.NotFound() : Results.Ok(new TodoResponse(todo));
        });

        group.MapPost("/", async (
            [FromBody] CreateTodoRequest request,
            ITodoRepository todos,
            IOperationRepository ops,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = Todo.Create(request.Title, DateTimeOffset.UtcNow);
            if (!result.IsSuccess)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["title"] = result.Errors });

            var (todo, _) = result.Value!;
            var operation = new TodoOperation
            {
                Id          = Guid.NewGuid(),
                Status      = "complete",
                ResultJson  = JsonSerializer.Serialize(new { id = todo.Id }),
                CreatedAt   = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await todos.AddAsync(todo, ct);    // stages Todo
            await ops.AddAsync(operation, ct); // stages Operation
            await ops.SaveAsync(ct);           // flushes both (shared DbContext)

            httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
        });

        group.MapPost("/{id:guid}/complete", async (
            Guid id,
            ITodoRepository todos,
            IOperationRepository ops,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var todo = await todos.GetByIdAsync(id, ct);
            if (todo is null) return Results.NotFound();

            var result = todo.Complete(DateTimeOffset.UtcNow);
            if (!result.IsSuccess)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { [""] = result.Errors });

            var operation = new TodoOperation
            {
                Id          = Guid.NewGuid(),
                Status      = "complete",
                CreatedAt   = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await ops.AddAsync(operation, ct);
            await ops.SaveAsync(ct); // flushes all (shared DbContext — saves todo state + operation)

            httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
        });

        group.MapPost("/{id:guid}/uncomplete", async (
            Guid id,
            ITodoRepository todos,
            IOperationRepository ops,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var todo = await todos.GetByIdAsync(id, ct);
            if (todo is null) return Results.NotFound();

            var result = todo.Uncomplete();
            if (!result.IsSuccess)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { [""] = result.Errors });

            var operation = new TodoOperation
            {
                Id          = Guid.NewGuid(),
                Status      = "complete",
                CreatedAt   = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await ops.AddAsync(operation, ct);
            await ops.SaveAsync(ct); // flushes all (shared DbContext — saves todo state + operation)

            httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ITodoRepository todos,
            IOperationRepository ops,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var todo = await todos.GetByIdAsync(id, ct);
            if (todo is null) return Results.NotFound();

            var result = todo.Delete(DateTimeOffset.UtcNow);
            if (!result.IsSuccess)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { [""] = result.Errors });

            var operation = new TodoOperation
            {
                Id          = Guid.NewGuid(),
                Status      = "complete",
                CreatedAt   = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await ops.AddAsync(operation, ct);
            await ops.SaveAsync(ct); // flushes all (shared DbContext — saves todo state + operation)

            httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
        });
    }
}

public record TodoResponse(
    Guid Id,
    string Title,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public TodoResponse(Todo todo)
        : this(todo.Id, todo.Title, todo.IsCompleted, todo.CreatedAt, todo.CompletedAt) { }
}

public record OperationAcceptedResponse(Guid OperationId, int RetryAfterMs);

public record CreateTodoRequest(string Title);
