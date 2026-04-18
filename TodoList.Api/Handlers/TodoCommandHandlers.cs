// TodoList.Api/Handlers/TodoCommandHandlers.cs
using System.Text.Json;
using TodoList.Api.Data;
using TodoList.Api.Operations;
using TodoList.Domain;
using TodoList.Domain.Aggregates;
using TodoList.Domain.Commands;
using TodoList.Domain.Events;
using Wolverine;
using Wolverine.Attributes;

namespace TodoList.Api.Handlers;

[WolverineHandler]
public static class TodoCommandHandlers
{
    public static async Task<object[]> Handle(CreateTodoCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var result = Todo.Create(cmd.Title, DateTimeOffset.UtcNow, cmd.UserId,
            cmd.CategoryId, cmd.DueDate, cmd.Notes, cmd.Progress);

        if (!result.IsSuccess)
        {
            await FailOperation(ops, cmd.OperationId, result.Errors);
            return [];
        }

        var (todo, events) = result.Value!;
        await repo.AddAsync(todo);
        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = todo.Id }));
        await repo.SaveAsync();

        return WrapEvents(events, cmd.UserId);
    }

    public static async Task<object[]> Handle(RenameTodoCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.Rename(cmd.NewTitle);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    public static async Task<object[]> Handle(CompleteTodoCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.Complete(DateTimeOffset.UtcNow);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    public static async Task<object[]> Handle(UncompleteTodoCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.Uncomplete();
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    public static async Task<object[]> Handle(DeleteTodoCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.Delete(DateTimeOffset.UtcNow);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    public static async Task<object[]> Handle(AssignCategoryCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.AssignCategory(cmd.CategoryId);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    public static async Task<object[]> Handle(UnassignCategoryCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.UnassignCategory();
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    public static async Task<object[]> Handle(SetDueDateCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.SetDueDate(cmd.DueDate);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    public static async Task<object[]> Handle(ClearDueDateCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.ClearDueDate();
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    public static async Task<object[]> Handle(UpdateNotesCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.UpdateNotes(cmd.Notes);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    public static async Task<object[]> Handle(UpdateProgressCommand cmd, ITodoRepository repo, IOperationRepository ops)
    {
        var todo = await repo.GetByIdAsync(cmd.TodoId);
        if (todo is null) { await FailOperation(ops, cmd.OperationId, "Todo not found"); return []; }
        if (cmd.ExpectedVersion > 0 && todo.Version != cmd.ExpectedVersion)
            return await ConflictOperation(ops, cmd.OperationId, cmd.TodoId, todo.Version);

        var result = todo.UpdateProgress(cmd.Progress);
        if (!result.IsSuccess) { await FailOperation(ops, cmd.OperationId, result.Errors); return []; }

        await CompleteOperation(ops, cmd.OperationId, JsonSerializer.Serialize(new { id = cmd.TodoId }));
        await repo.SaveAsync();
        return WrapEvents(result.Value!, cmd.UserId);
    }

    // Wolverine cascades: wrap domain events with UserId for projection handlers
    private static object[] WrapEvents(IReadOnlyList<IDomainEvent> events, string userId) =>
        events.Select(e => (object)new UserScopedEvent(userId, e)).ToArray();

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

    private static async Task FailOperation(IOperationRepository ops, Guid operationId, string[] errors)
    {
        await FailOperation(ops, operationId, string.Join("; ", errors));
    }

    private static async Task<object[]> ConflictOperation(IOperationRepository ops, Guid operationId, Guid todoId, int serverVersion)
    {
        var op = await ops.GetByIdAsync(operationId);
        if (op is not null)
        {
            op.Status = "failed";
            op.FailureReason = $"Version conflict: expected version does not match server version {serverVersion}";
            op.CompletedAt = DateTimeOffset.UtcNow;
            await ops.SaveAsync();
        }
        return [];
    }
}

/// <summary>
/// Wraps a domain event with the UserId that caused it.
/// Projection handlers subscribe to this to know which user's projections to update.
/// </summary>
public record UserScopedEvent(string UserId, IDomainEvent Event);
