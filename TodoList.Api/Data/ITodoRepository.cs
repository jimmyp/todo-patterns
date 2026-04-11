using TodoList.Domain.Aggregates;

namespace TodoList.Api.Data;

public interface ITodoRepository
{
    Task<List<Todo>> GetAllAsync(CancellationToken ct = default);
    Task<Todo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Todo todo, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
