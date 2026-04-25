using Microsoft.EntityFrameworkCore;
using TodoList.Api.Data.Projections;
using TodoList.Domain.Aggregates;
using TodoList.Api.Operations;

namespace TodoList.Api.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();
    public DbSet<TodoOperation> Operations => Set<TodoOperation>();
    public DbSet<CategoryListEntity> CategoryLists => Set<CategoryListEntity>();
    public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();
    public DbSet<TodoSummaryEntity> TodoSummaries => Set<TodoSummaryEntity>();
    public DbSet<CategorySummaryEntity> CategorySummaries => Set<CategorySummaryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Title).HasMaxLength(500).IsRequired();
            b.Property(t => t.Notes).HasMaxLength(2000);
            b.Property(t => t.UserId).HasMaxLength(200);
            b.HasQueryFilter(t => !t.IsDeleted);
        });

        modelBuilder.Entity<TodoOperation>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Status).HasMaxLength(20).IsRequired();
            b.Property(o => o.FailureReason).HasMaxLength(2000);
            b.Property(o => o.FailureCode).HasMaxLength(50);
        });

        modelBuilder.Entity<CategoryListEntity>(b =>
        {
            b.HasKey(cl => cl.UserId);
            b.HasMany(cl => cl.Categories).WithOne().HasForeignKey(c => c.UserId)
                .HasPrincipalKey(cl => cl.UserId);
        });

        modelBuilder.Entity<CategoryEntity>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(50).IsRequired();
            b.Property(c => c.Color).HasMaxLength(10).IsRequired();
            b.Property(c => c.Icon).HasMaxLength(50).IsRequired();
            b.HasIndex(c => c.UserId);
        });

        modelBuilder.Entity<TodoSummaryEntity>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Title).HasMaxLength(500).IsRequired();
            b.Property(t => t.CategoryName).HasMaxLength(50);
            b.Property(t => t.CategoryColor).HasMaxLength(10);
            b.HasIndex(t => t.UserId);
        });

        modelBuilder.Entity<CategorySummaryEntity>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(50).IsRequired();
            b.Property(c => c.Color).HasMaxLength(10).IsRequired();
            b.Property(c => c.Icon).HasMaxLength(50).IsRequired();
            b.HasIndex(c => c.UserId);
        });
    }
}
