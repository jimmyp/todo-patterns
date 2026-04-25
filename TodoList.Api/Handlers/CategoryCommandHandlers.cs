// TodoList.Api/Handlers/CategoryCommandHandlers.cs
using System.Text.Json;
using TodoList.Api.Data;
using TodoList.Api.Operations;
using TodoList.Domain;
using TodoList.Domain.Aggregates;
using TodoList.Domain.Commands;
using TodoList.Domain.Events;
using Wolverine.Attributes;

namespace TodoList.Api.Handlers;

[WolverineHandler]
public static class CategoryCommandHandlers
{
    public static async Task<object[]> Handle(AddCategoryCommand cmd, ICategoryListRepository repo, IOperationRepository ops)
    {
        var seedEvts = new List<IDomainEvent>();
        var list = await repo.GetByUserIdAsync(cmd.UserId);
        if (list is null)
        {
            // First-use auto-seed: create CategoryList with default categories for this user.
            // Spec S1/S2: no explicit seed endpoint — seeding is a side-effect of first mutation.
            var (newList, seedEvents) = CategoryList.Create(cmd.UserId);
            await repo.AddAsync(newList);
            list = newList;
            seedEvts.AddRange(seedEvents);
        }
        else if (cmd.ExpectedVersion > 0 && list.Version != cmd.ExpectedVersion)
        {
            await ConflictOperation(ops, cmd.OperationId, list.Version);
            return [];
        }

        var result = list.AddCategory(cmd.Name, cmd.Color, cmd.Icon);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await repo.SaveAsync();
        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = result.Value!.CategoryId }));
        seedEvts.Add(result.Value!);
        return WrapEvents(seedEvts, cmd.UserId, list.Version);
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
        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.CategoryId }));
        return WrapEvents([result.Value!], cmd.UserId, list.Version);
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
        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.CategoryId }));
        return WrapEvents([result.Value!], cmd.UserId, list.Version);
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
        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.CategoryId }));
        return WrapEvents([result.Value!], cmd.UserId, list.Version);
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
        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.CategoryId }));
        return WrapEvents([result.Value!], cmd.UserId, list.Version);
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
        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.CategoryId }));
        return WrapEvents([result.Value!], cmd.UserId, list.Version);
    }

    /// <summary>
    /// CategoryList is a single per-user aggregate. We use the synthetic aggregate id
    /// "user-category-list" on the wire (SignalR groups are already per-user, so we don't
    /// need userId in the id). Must match the client's CategoryListAggregateId constant.
    /// </summary>
    public const string CategoryListAggregateId = "user-category-list";

    private static object[] WrapEvents(IReadOnlyList<IDomainEvent> events, string userId, int aggregateVersion) =>
        events.Select(e => (object)new UserScopedEvent(userId, CategoryListAggregateId, aggregateVersion, e)).ToArray();

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

    private static async Task FailOperation(IOperationRepository ops, Guid operationId, string error, string code = FailureCodes.NotFound)
    {
        var op = await ops.GetByIdAsync(operationId);
        if (op is not null)
        {
            op.Status = "failed";
            op.FailureReason = error;
            op.FailureCode = code;
            op.CompletedAt = DateTimeOffset.UtcNow;
            await ops.SaveAsync();
        }
    }

    private static async Task FailOperation(IOperationRepository ops, Guid operationId, string[] errors) =>
        await FailOperation(ops, operationId, string.Join("; ", errors), FailureCodes.ValidationError);

    private static async Task ConflictOperation(IOperationRepository ops, Guid operationId, int serverVersion)
    {
        var op = await ops.GetByIdAsync(operationId);
        if (op is not null)
        {
            op.Status = "failed";
            op.FailureReason = $"Version conflict: expected version does not match server version {serverVersion}";
            op.FailureCode = FailureCodes.VersionConflict;
            op.CompletedAt = DateTimeOffset.UtcNow;
            await ops.SaveAsync();
        }
    }
}
