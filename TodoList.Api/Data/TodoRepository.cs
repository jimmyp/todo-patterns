using Microsoft.EntityFrameworkCore;
using TodoList.Api.Domain;

namespace TodoList.Api.Data;

public class TodoRepository(TodoDbContext db) : ITodoRepository
{
    public Task<List<Todo>> GetAllAsync(CancellationToken ct) =>
        db.Todos.ToListAsync(ct);

    public Task<Todo?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Todos.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task AddAsync(Todo todo, CancellationToken ct) =>
        await db.Todos.AddAsync(todo, ct);

    public Task SaveAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}
