// TodoList.Api/Endpoints/TodoEndpoints.cs
using System.Security.Claims;
using TodoList.Api.Data;
using TodoList.Api.Operations;
using TodoList.Domain.Aggregates;
using TodoList.Domain.Commands;
using Wolverine;

namespace TodoList.Api.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/todos").RequireAuthorization();

        group.MapGet("/", async (TodoDbContext db, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var summaries = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(db.TodoSummaries
                    .Where(t => t.UserId == userId)
                    .OrderBy(t => t.CreatedAt));

            var now = DateTimeOffset.UtcNow;
            return Results.Ok(summaries.Select(t => new
            {
                t.Id, t.Title, t.IsCompleted, t.CategoryId, t.CategoryName, t.CategoryColor,
                t.DueDate, IsOverdue = t.DueDate.HasValue && !t.IsCompleted && t.DueDate < now,
                t.Progress, t.CreatedAt, t.CompletedAt, t.Version
            }));
        });

        group.MapGet("/{id:guid}", async (Guid id, ITodoRepository repo, CancellationToken ct) =>
        {
            var todo = await repo.GetByIdAsync(id, ct);
            return todo is null ? Results.NotFound() : Results.Ok(new TodoResponse(todo));
        });

        group.MapPost("/", async (
            CreateTodoRequest request,
            IMessageBus bus,
            IOperationRepository ops,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var operationId = Guid.NewGuid();
            await ops.AddAsync(new TodoOperation { Id = operationId, UserId = userId, Status = "processing", CreatedAt = DateTimeOffset.UtcNow });
            await ops.SaveAsync();

            await bus.PublishAsync(new CreateTodoCommand(
                request.Title, userId, operationId,
                request.CategoryId, request.DueDate, request.Notes, request.Progress ?? 0));

            ctx.Response.Headers["X-Retry-After-Ms"] = "200";
            return Results.Accepted(
                $"/todos/operations/{operationId}",
                new OperationAcceptedResponse(operationId, RetryAfterMs: 200));
        });

        group.MapPost("/{id:guid}/complete", async (Guid id, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new CompleteTodoCommand(id, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id:guid}/uncomplete", async (Guid id, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new UncompleteTodoCommand(id, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapDelete("/{id:guid}", async (Guid id, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new DeleteTodoCommand(id, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id:guid}/rename", async (Guid id, RenameTodoRequest req, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new RenameTodoCommand(id, req.Title, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id:guid}/assign-category", async (Guid id, AssignCategoryRequest req, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new AssignCategoryCommand(id, req.CategoryId, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id:guid}/unassign-category", async (Guid id, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new UnassignCategoryCommand(id, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id:guid}/set-due-date", async (Guid id, SetDueDateRequest req, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new SetDueDateCommand(id, req.DueDate, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id:guid}/clear-due-date", async (Guid id, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new ClearDueDateCommand(id, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id:guid}/update-notes", async (Guid id, UpdateNotesRequest req, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new UpdateNotesCommand(id, req.Notes, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id:guid}/update-progress", async (Guid id, UpdateProgressRequest req, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new UpdateProgressCommand(id, req.Progress, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });
    }

    private static string GetUserId(HttpContext ctx) =>
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

    private static int GetExpectedVersion(HttpContext ctx) =>
        int.TryParse(ctx.Request.Headers["X-Expected-Version"].FirstOrDefault(), out var v) ? v : 0;

    private static async Task<(Guid operationId, int expectedVersion)> CreateOperation(IOperationRepository ops, HttpContext ctx)
    {
        var operationId = Guid.NewGuid();
        var expectedVersion = GetExpectedVersion(ctx);
        await ops.AddAsync(new TodoOperation { Id = operationId, UserId = GetUserId(ctx), Status = "processing", CreatedAt = DateTimeOffset.UtcNow });
        await ops.SaveAsync();
        return (operationId, expectedVersion);
    }

    private static IResult Accepted(HttpContext ctx, Guid operationId)
    {
        ctx.Response.Headers["X-Retry-After-Ms"] = "200";
        return Results.Accepted(
            $"/todos/operations/{operationId}",
            new OperationAcceptedResponse(operationId, RetryAfterMs: 200));
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

public record CreateTodoRequest(
    string Title,
    Guid? CategoryId = null,
    DateTimeOffset? DueDate = null,
    string? Notes = null,
    int? Progress = null);

file record RenameTodoRequest(string Title);
file record AssignCategoryRequest(Guid CategoryId);
file record SetDueDateRequest(DateTimeOffset DueDate);
file record UpdateNotesRequest(string? Notes);
file record UpdateProgressRequest(int Progress);
