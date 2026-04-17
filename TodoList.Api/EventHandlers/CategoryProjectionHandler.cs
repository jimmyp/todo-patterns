// TodoList.Api/EventHandlers/CategoryProjectionHandler.cs
using TodoList.Api.Data;
using TodoList.Api.Data.Projections;
using TodoList.Domain;
using TodoList.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.EventHandlers;

/// <summary>
/// Wolverine event handler — subscribes to category domain events (cascaded from command handlers)
/// and updates CategorySummary projections.
/// Category events already carry UserId so they don't need a UserScopedEvent wrapper.
/// </summary>
public class CategoryProjectionHandler(TodoDbContext db)
{
    public async Task Handle(CategoryAddedEvent e)
    {
        db.CategorySummaries.Add(new CategorySummaryEntity
        {
            Id = e.CategoryId,
            UserId = e.UserId,
            Name = e.Name,
            Color = e.Color,
            Icon = e.Icon,
            Order = e.Order,
            TodoCount = 0,
            Version = 1
        });
        await db.SaveChangesAsync();
    }

    public async Task Handle(CategoryRenamedEvent e)
    {
        var cat = await db.CategorySummaries.FindAsync(e.CategoryId);
        if (cat is not null)
        {
            cat.Name = e.NewName;
            cat.Version++;
            var todos = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
            await todos.ForEachAsync(t => t.CategoryName = e.NewName);
        }
        await db.SaveChangesAsync();
    }

    public async Task Handle(CategoryColorChangedEvent e)
    {
        var cat = await db.CategorySummaries.FindAsync(e.CategoryId);
        if (cat is not null)
        {
            cat.Color = e.NewColor;
            cat.Version++;
            var todos = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
            await todos.ForEachAsync(t => t.CategoryColor = e.NewColor);
        }
        await db.SaveChangesAsync();
    }

    public async Task Handle(CategoryIconChangedEvent e)
    {
        var cat = await db.CategorySummaries.FindAsync(e.CategoryId);
        if (cat is not null) { cat.Icon = e.NewIcon; cat.Version++; }
        await db.SaveChangesAsync();
    }

    public async Task Handle(CategoryReorderedEvent e)
    {
        var cat = await db.CategorySummaries.FindAsync(e.CategoryId);
        if (cat is not null) { cat.Order = e.NewOrder; cat.Version++; }
        await db.SaveChangesAsync();
    }

    public async Task Handle(CategoryRemovedEvent e)
    {
        var cat = await db.CategorySummaries.FindAsync(e.CategoryId);
        if (cat is not null) db.CategorySummaries.Remove(cat);
        var todos = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
        await todos.ForEachAsync(t => { t.CategoryId = null; t.CategoryName = null; t.CategoryColor = null; });
        await db.SaveChangesAsync();
    }
}
