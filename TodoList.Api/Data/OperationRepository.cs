using Microsoft.EntityFrameworkCore;
using TodoList.Api.Operations;

namespace TodoList.Api.Data;

public class OperationRepository(TodoDbContext db) : IOperationRepository
{
    public Task<TodoOperation?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Operations.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task AddAsync(TodoOperation operation, CancellationToken ct) =>
        await db.Operations.AddAsync(operation, ct);

    public Task SaveAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}
