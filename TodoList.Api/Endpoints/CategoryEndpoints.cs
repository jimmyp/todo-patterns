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
        var group = app.MapGroup("/categories").RequireAuthorization();

        group.MapGet("/", async (TodoDbContext db, ICategoryListRepository listRepo, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);

            // Auto-seed on first access: if the user has no CategoryList yet, create
            // one with the default categories synchronously so the first GET returns them.
            // Race safety (S13): two concurrent first-loads both see list is null. The first
            // one to commit wins — `CategoryListEntity.UserId` is the PK so the second
            // commit raises a unique constraint violation. Catch + re-read is enough; no
            // SemaphoreSlim needed because uniqueness lives at the storage boundary.
            var list = await listRepo.GetByUserIdAsync(userId);
            if (list is null)
            {
                try
                {
                    var (newList, events) = TodoList.Domain.Aggregates.CategoryList.Create(userId);
                    await listRepo.AddAsync(newList);
                    foreach (var e in events)
                    {
                        db.CategorySummaries.Add(new Data.Projections.CategorySummaryEntity
                        {
                            Id = e.CategoryId,
                            UserId = e.UserId,
                            Name = e.Name,
                            Color = e.Color,
                            Icon = e.Icon,
                            Order = e.Order,
                            TodoCount = 0,
                            Version = 1
                        });
                    }
                    await listRepo.SaveAsync();
                    await db.SaveChangesAsync();
                    list = newList;
                }
                catch (DbUpdateException)
                {
                    // Another request seeded it first — re-read and continue.
                    db.ChangeTracker.Clear();
                    list = await listRepo.GetByUserIdAsync(userId);
                }
            }

            var categories = await db.CategorySummaries
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Order)
                .ToListAsync();
            return Results.Ok(new
            {
                version = list?.Version ?? 0,
                categories = categories.Select(c => new
                {
                    c.Id, c.Name, c.Color, c.Icon, c.Order, c.TodoCount, c.Version
                })
            });
        });

        group.MapPost("/", async (AddCategoryRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new AddCategoryCommand(request.Name, request.Color, request.Icon, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id}/rename", async (Guid id, RenameCategoryRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new RenameCategoryCommand(id, request.Name, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id}/change-color", async (Guid id, ChangeCategoryColorRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new ChangeCategoryColorCommand(id, request.Color, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id}/change-icon", async (Guid id, ChangeCategoryIconRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new ChangeCategoryIconCommand(id, request.Icon, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapPost("/{id}/reorder", async (Guid id, ReorderCategoryRequest request, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
        {
            var (operationId, expectedVersion) = await CreateOperation(ops, ctx);
            await bus.PublishAsync(new ReorderCategoryCommand(id, request.Order, GetUserId(ctx), operationId, expectedVersion));
            return Accepted(ctx, operationId);
        });

        group.MapDelete("/{id}", async (Guid id, IMessageBus bus, IOperationRepository ops, HttpContext ctx) =>
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

    private record AddCategoryRequest(string Name, string Color, string Icon);
    private record RenameCategoryRequest(string Name);
    private record ChangeCategoryColorRequest(string Color);
    private record ChangeCategoryIconRequest(string Icon);
    private record ReorderCategoryRequest(int Order);
}
