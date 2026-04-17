// TodoList.Api/Endpoints/CategoryEndpoints.cs
using System.Security.Claims;
using TodoList.Api.Data;
using TodoList.Api.Operations;
using TodoList.Domain.Commands;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace TodoList.Api.Endpoints;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this WebApplication app)
    {
        app.MapGet("/categories", async (TodoDbContext db, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var categories = await db.CategorySummaries
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Order)
                .ToListAsync();
            return Results.Ok(categories.Select(c => new
            {
                c.Id, c.Name, c.Color, c.Icon, c.Order, c.TodoCount, c.Version
            }));
        });

        // Seed default categories for user (called on first login)
        app.MapPost("/categories/seed", async (IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, _) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new SeedCategoriesCommand(GetUserId(ctx), operationId));
            return Accepted(ctx, operationId);
        });

        app.MapPost("/categories", async (AddCategoryRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new AddCategoryCommand(request.Name, request.Color, request.Icon, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        app.MapPost("/categories/{id}/rename", async (Guid id, RenameCategoryRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new RenameCategoryCommand(id, request.Name, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        app.MapPost("/categories/{id}/change-color", async (Guid id, ChangeCategoryColorRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new ChangeCategoryColorCommand(id, request.Color, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        app.MapPost("/categories/{id}/change-icon", async (Guid id, ChangeCategoryIconRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new ChangeCategoryIconCommand(id, request.Icon, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        app.MapPost("/categories/{id}/reorder", async (Guid id, ReorderCategoryRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new ReorderCategoryCommand(id, request.Order, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        app.MapDelete("/categories/{id}", async (Guid id, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new RemoveCategoryCommand(id, GetUserId(ctx), operationId, expectedVersion));
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
        await ops.AddAsync(new TodoOperation { Id = operationId, Status = "processing", CreatedAt = DateTimeOffset.UtcNow });
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

    private record AddCategoryRequest(string Name, string Color, string Icon);
    private record RenameCategoryRequest(string Name);
    private record ChangeCategoryColorRequest(string Color);
    private record ChangeCategoryIconRequest(string Icon);
    private record ReorderCategoryRequest(int Order);
}
