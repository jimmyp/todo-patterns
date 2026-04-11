// TodoList.Api/Endpoints/CategoryEndpoints.cs
using TodoList.Api.Data;
using TodoList.Api.Data.Projections;
using TodoList.Api.EventHandlers;
using TodoList.Api.Operations;
using TodoList.Domain.Aggregates;
using TodoList.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.Endpoints;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this WebApplication app)
    {
        app.MapGet("/categories", async (TodoDbContext db, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var categories = await db.CategorySummaries
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Order)
                .ToListAsync();
            return Results.Ok(categories.Select(c => new
            {
                c.Id, c.Name, c.Color, c.Icon, c.Order, c.TodoCount
            }));
        });

        // Seed default categories for user (called on first login)
        app.MapPost("/categories/seed", async (
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            TodoDbContext db,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var existing = await repo.GetByUserIdAsync(userId);
            if (existing is not null) return Results.Ok();

            var (list, events) = CategoryList.Create(userId);
            await repo.AddAsync(list);
            await repo.SaveAsync();

            foreach (var evt in events)
                await projectionHandler.HandleAsync(evt);

            return Results.Ok();
        });

        app.MapPost("/categories", async (
            AddCategoryRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound("CategoryList not found — call /categories/seed first");

            var result = list.AddCategory(request.Name, request.Color, request.Icon);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), result.Value!.CategoryId.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();

            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new { operationId = operation.Id });
        }).AddEndpointFilter(async (ctx, next) =>
        {
            var result = await next(ctx);
            return result;
        });

        app.MapPost("/categories/{id}/rename", async (
            Guid id,
            RenameCategoryRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.RenameCategory(id, request.Name);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });

        app.MapPost("/categories/{id}/change-color", async (
            Guid id,
            ChangeCategoryColorRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.ChangeColor(id, request.Color);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });

        app.MapPost("/categories/{id}/change-icon", async (
            Guid id,
            ChangeCategoryIconRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.ChangeIcon(id, request.Icon);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });

        app.MapPost("/categories/{id}/reorder", async (
            Guid id,
            ReorderCategoryRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.Reorder(id, request.Order);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });

        app.MapDelete("/categories/{id}", async (
            Guid id,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.RemoveCategory(id);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });
    }

    private record AddCategoryRequest(string Name, string Color, string Icon);
    private record RenameCategoryRequest(string Name);
    private record ChangeCategoryColorRequest(string Color);
    private record ChangeCategoryIconRequest(string Icon);
    private record ReorderCategoryRequest(int Order);
}
