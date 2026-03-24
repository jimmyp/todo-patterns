using TodoList.Api.Operations;

namespace TodoList.Api.Data;

public interface IOperationRepository
{
    Task<TodoOperation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(TodoOperation operation, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
