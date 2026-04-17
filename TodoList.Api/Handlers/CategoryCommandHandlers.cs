// TodoList.Api/Handlers/CategoryCommandHandlers.cs
using TodoList.Api.Data;
using TodoList.Api.Operations;
using TodoList.Domain;
using TodoList.Domain.Aggregates;
using TodoList.Domain.Commands;
using TodoList.Domain.Events;

namespace TodoList.Api.Handlers;

public static class CategoryCommandHandlers
{
    public static async Task<object[]> Handle(SeedCategoriesCommand cmd, ICategoryListRepository repo, IOperationRepository ops)
    {
        var existing = await repo.GetByUserIdAsync(cmd.UserId);
        if (existing is not null)
        {
            await CompleteOperation(ops, cmd.OperationId, "already-seeded");
            return [];
        }

        var (list, events) = CategoryList.Create(cmd.UserId);
        await repo.AddAsync(list);
        await repo.SaveAsync();
        await CompleteOperation(ops, cmd.OperationId, "seeded");
        return events.Select(e => (object)e).ToArray();
    }

    public static async Task<object[]> Handle(AddCategoryCommand cmd, ICategoryListRepository repo, IOperationRepository ops)
    {
        var list = await repo.GetByUserIdAsync(cmd.UserId);
        if (list is null) { await FailOperation(ops, cmd.OperationId, "CategoryList not found"); return []; }
        if (cmd.ExpectedVersion > 0 && list.Version != cmd.ExpectedVersion)
        {
            await ConflictOperation(ops, cmd.OperationId, list.Version);
            return [];
        }

        var result = list.AddCategory(cmd.Name, cmd.Color, cmd.Icon);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await repo.SaveAsync();
        await CompleteOperation(ops, cmd.OperationId, result.Value!.CategoryId.ToString());
        return [result.Value!];
    }

    public static async Task<object[]> Handle(RenameCategoryCommand cmd, ICategoryListRepository repo, IOperationRepository ops)
    {
        var list = await repo.GetByUserIdAsync(cmd.UserId);
        if (list is null) { await FailOperation(ops, cmd.OperationId, "CategoryList not found"); return []; }
        if (cmd.ExpectedVersion > 0 && list.Version != cmd.ExpectedVersion)
        {
            await ConflictOperation(ops, cmd.OperationId, list.Version);
            return [];
        }

        var result = list.RenameCategory(cmd.CategoryId, cmd.NewName);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await repo.SaveAsync();
        await CompleteOperation(ops, cmd.OperationId, cmd.CategoryId.ToString());
        return [result.Value!];
    }

    public static async Task<object[]> Handle(ChangeCategoryColorCommand cmd, ICategoryListRepository repo, IOperationRepository ops)
    {
        var list = await repo.GetByUserIdAsync(cmd.UserId);
        if (list is null) { await FailOperation(ops, cmd.OperationId, "CategoryList not found"); return []; }
        if (cmd.ExpectedVersion > 0 && list.Version != cmd.ExpectedVersion)
        {
            await ConflictOperation(ops, cmd.OperationId, list.Version);
            return [];
        }

        var result = list.ChangeColor(cmd.CategoryId, cmd.Color);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await repo.SaveAsync();
        await CompleteOperation(ops, cmd.OperationId, cmd.CategoryId.ToString());
        return [result.Value!];
    }

    public static async Task<object[]> Handle(ChangeCategoryIconCommand cmd, ICategoryListRepository repo, IOperationRepository ops)
    {
        var list = await repo.GetByUserIdAsync(cmd.UserId);
        if (list is null) { await FailOperation(ops, cmd.OperationId, "CategoryList not found"); return []; }
        if (cmd.ExpectedVersion > 0 && list.Version != cmd.ExpectedVersion)
        {
            await ConflictOperation(ops, cmd.OperationId, list.Version);
            return [];
        }

        var result = list.ChangeIcon(cmd.CategoryId, cmd.Icon);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await repo.SaveAsync();
        await CompleteOperation(ops, cmd.OperationId, cmd.CategoryId.ToString());
        return [result.Value!];
    }

    public static async Task<object[]> Handle(ReorderCategoryCommand cmd, ICategoryListRepository repo, IOperationRepository ops)
    {
        var list = await repo.GetByUserIdAsync(cmd.UserId);
        if (list is null) { await FailOperation(ops, cmd.OperationId, "CategoryList not found"); return []; }
        if (cmd.ExpectedVersion > 0 && list.Version != cmd.ExpectedVersion)
        {
            await ConflictOperation(ops, cmd.OperationId, list.Version);
            return [];
        }

        var result = list.Reorder(cmd.CategoryId, cmd.Order);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await repo.SaveAsync();
        await CompleteOperation(ops, cmd.OperationId, cmd.CategoryId.ToString());
        return [result.Value!];
    }

    public static async Task<object[]> Handle(RemoveCategoryCommand cmd, ICategoryListRepository repo, IOperationRepository ops)
    {
        var list = await repo.GetByUserIdAsync(cmd.UserId);
        if (list is null) { await FailOperation(ops, cmd.OperationId, "CategoryList not found"); return []; }
        if (cmd.ExpectedVersion > 0 && list.Version != cmd.ExpectedVersion)
        {
            await ConflictOperation(ops, cmd.OperationId, list.Version);
            return [];
        }

        var result = list.RemoveCategory(cmd.CategoryId);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await repo.SaveAsync();
        await CompleteOperation(ops, cmd.OperationId, cmd.CategoryId.ToString());
        return [result.Value!];
    }

    private static async Task CompleteOperation(IOperationRepository ops, Guid operationId, string resultJson)
    {
        var op = await ops.GetByIdAsync(operationId);
        if (op is not null)
        {
            op.Status = "complete";
            op.ResultJson = resultJson;
            op.CompletedAt = DateTimeOffset.UtcNow;
            await ops.SaveAsync();
        }
    }

    private static async Task FailOperation(IOperationRepository ops, Guid operationId, string error)
    {
        var op = await ops.GetByIdAsync(operationId);
        if (op is not null)
        {
            op.Status = "failed";
            op.FailureReason = error;
            op.CompletedAt = DateTimeOffset.UtcNow;
            await ops.SaveAsync();
        }
    }

    private static async Task FailOperation(IOperationRepository ops, Guid operationId, string[] errors) =>
        await FailOperation(ops, operationId, string.Join("; ", errors));

    private static async Task ConflictOperation(IOperationRepository ops, Guid operationId, int serverVersion)
    {
        var op = await ops.GetByIdAsync(operationId);
        if (op is not null)
        {
            op.Status = "failed";
            op.FailureReason = $"Version conflict: expected version does not match server version {serverVersion}";
            op.CompletedAt = DateTimeOffset.UtcNow;
            await ops.SaveAsync();
        }
    }
}
