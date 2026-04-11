using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoList.Api.Data;
using TodoList.Domain.Aggregates;
using TodoList.Api.Operations;
using TodoList.Api.EventHandlers;

namespace TodoList.Api.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/todos");

        group.MapGet("/", async (TodoDbContext db, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var summaries = await db.TodoSummaries
                .Where(t => t.UserId == userId)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();

            var now = DateTimeOffset.UtcNow;
            return Results.Ok(summaries.Select(t => new
            {
                t.Id, t.Title, t.IsCompleted, t.CategoryId, t.CategoryName, t.CategoryColor,
                t.DueDate, IsOverdue = t.DueDate.HasValue && !t.IsCompleted && t.DueDate < now,
                t.Progress, t.CreatedAt, t.CompletedAt
            }));
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
            TodoProjectionHandler projHandler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var result = Todo.Create(request.Title, DateTimeOffset.UtcNow);
            if (!result.IsSuccess)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["title"] = result.Errors });

            var (todo, events) = result.Value!;
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

            // Write projection
            foreach (var evt in events)
                await projHandler.HandleAsync(userId, evt);

            httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
        });

        group.MapPost("/{id:guid}/complete", async (
            Guid id,
            ITodoRepository todos,
            IOperationRepository ops,
            TodoProjectionHandler projHandler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
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

            foreach (var evt in result.Value!)
                await projHandler.HandleAsync(userId, evt);

            httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
        });

        group.MapPost("/{id:guid}/uncomplete", async (
            Guid id,
            ITodoRepository todos,
            IOperationRepository ops,
            TodoProjectionHandler projHandler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
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

            foreach (var evt in result.Value!)
                await projHandler.HandleAsync(userId, evt);

            httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ITodoRepository todos,
            IOperationRepository ops,
            TodoProjectionHandler projHandler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
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

            foreach (var evt in result.Value!)
                await projHandler.HandleAsync(userId, evt);

            httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
        });

        // Extended endpoints
        group.MapPost("/{id:guid}/rename", async (
            Guid id, RenameTodoRequest req,
            ITodoRepository repo, TodoProjectionHandler projHandler,
            IOperationRepository opRepo, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var todo = await repo.GetByIdAsync(id);
            if (todo is null) return Results.NotFound();

            var result = todo.Rename(req.Title);
            if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

            var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(op); await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
        });

        group.MapPost("/{id:guid}/assign-category", async (
            Guid id, AssignCategoryRequest req,
            ITodoRepository repo, TodoProjectionHandler projHandler,
            IOperationRepository opRepo, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var todo = await repo.GetByIdAsync(id);
            if (todo is null) return Results.NotFound();

            var result = todo.AssignCategory(req.CategoryId);
            if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

            var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(op); await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
        });

        group.MapPost("/{id:guid}/unassign-category", async (
            Guid id,
            ITodoRepository repo, TodoProjectionHandler projHandler,
            IOperationRepository opRepo, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var todo = await repo.GetByIdAsync(id);
            if (todo is null) return Results.NotFound();

            var result = todo.UnassignCategory();
            if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

            var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(op); await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
        });

        group.MapPost("/{id:guid}/set-due-date", async (
            Guid id, SetDueDateRequest req,
            ITodoRepository repo, TodoProjectionHandler projHandler,
            IOperationRepository opRepo, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var todo = await repo.GetByIdAsync(id);
            if (todo is null) return Results.NotFound();

            var result = todo.SetDueDate(req.DueDate);
            if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

            var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(op); await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
        });

        group.MapPost("/{id:guid}/clear-due-date", async (
            Guid id,
            ITodoRepository repo, TodoProjectionHandler projHandler,
            IOperationRepository opRepo, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var todo = await repo.GetByIdAsync(id);
            if (todo is null) return Results.NotFound();

            var result = todo.ClearDueDate();
            if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

            var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(op); await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
        });

        group.MapPost("/{id:guid}/update-notes", async (
            Guid id, UpdateNotesRequest req,
            ITodoRepository repo, TodoProjectionHandler projHandler,
            IOperationRepository opRepo, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var todo = await repo.GetByIdAsync(id);
            if (todo is null) return Results.NotFound();

            var result = todo.UpdateNotes(req.Notes);
            if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

            var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(op); await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
        });

        group.MapPost("/{id:guid}/update-progress", async (
            Guid id, UpdateProgressRequest req,
            ITodoRepository repo, TodoProjectionHandler projHandler,
            IOperationRepository opRepo, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var todo = await repo.GetByIdAsync(id);
            if (todo is null) return Results.NotFound();

            var result = todo.UpdateProgress(req.Progress);
            if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

            var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(op); await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
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

file record RenameTodoRequest(string Title);
file record AssignCategoryRequest(Guid CategoryId);
file record SetDueDateRequest(DateTimeOffset DueDate);
file record UpdateNotesRequest(string? Notes);
file record UpdateProgressRequest(int Progress);
