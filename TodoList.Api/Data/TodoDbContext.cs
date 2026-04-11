using Microsoft.EntityFrameworkCore;
using TodoList.Domain.Aggregates;
using TodoList.Api.Operations;

namespace TodoList.Api.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();
    public DbSet<TodoOperation> Operations => Set<TodoOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Title).HasMaxLength(500).IsRequired();
            b.HasQueryFilter(t => !t.IsDeleted);
        });

        modelBuilder.Entity<TodoOperation>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Status).HasMaxLength(20).IsRequired();
            b.Property(o => o.FailureReason).HasMaxLength(2000);
        });
    }
}
