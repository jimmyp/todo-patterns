// TodoList.Api/EventHandlers/CategoryProjectionHandler.cs
using TodoList.Api.Data;
using TodoList.Api.Data.Projections;
using TodoList.Domain;
using TodoList.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.EventHandlers;

public class CategoryProjectionHandler(TodoDbContext db)
{
    public async Task HandleAsync(IDomainEvent evt)
    {
        switch (evt)
        {
            case CategoryAddedEvent e:
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
                break;

            case CategoryRenamedEvent e:
                var cat = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat is not null)
                {
                    cat.Name = e.NewName;
                    cat.Version++;
                    // Update denormalized name in TodoSummaries
                    var todos = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
                    await todos.ForEachAsync(t => t.CategoryName = e.NewName);
                }
                break;

            case CategoryColorChangedEvent e:
                var cat2 = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat2 is not null)
                {
                    cat2.Color = e.NewColor;
                    cat2.Version++;
                    var todos = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
                    await todos.ForEachAsync(t => t.CategoryColor = e.NewColor);
                }
                break;

            case CategoryIconChangedEvent e:
                var cat3 = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat3 is not null) { cat3.Icon = e.NewIcon; cat3.Version++; }
                break;

            case CategoryReorderedEvent e:
                var cat4 = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat4 is not null) { cat4.Order = e.NewOrder; cat4.Version++; }
                break;

            case CategoryRemovedEvent e:
                var cat5 = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat5 is not null) db.CategorySummaries.Remove(cat5);
                // Unassign todos from this category
                var todos2 = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
                await todos2.ForEachAsync(t => { t.CategoryId = null; t.CategoryName = null; t.CategoryColor = null; });
                break;
        }

        await db.SaveChangesAsync();
    }
}
