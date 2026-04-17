// TodoList.Api/Handlers/CategoryRemovedCascadeHandler.cs
using TodoList.Api.Data;
using TodoList.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.Handlers;

/// <summary>
/// Subscribes to CategoryRemovedEvent. Unassigns the removed category from all
/// todos that reference it. Each unassignment produces a TodoCategoryUnassignedEvent
/// that flows through Wolverine to update the TodoSummary projections.
/// </summary>
public static class CategoryRemovedCascadeHandler
{
    public static async Task<object[]> Handle(CategoryRemovedEvent evt, ITodoRepository repo, TodoDbContext db)
    {
        var todos = await db.Todos
            .Where(t => t.CategoryId == evt.CategoryId && !t.IsDeleted)
            .ToListAsync();

        var cascadedEvents = new List<object>();
        foreach (var todo in todos)
        {
            var result = todo.UnassignCategory();
            if (result.IsSuccess)
            {
                foreach (var domainEvt in result.Value!)
                    cascadedEvents.Add(new UserScopedEvent(evt.UserId, domainEvt));
            }
        }

        if (cascadedEvents.Count > 0)
            await repo.SaveAsync();

        return cascadedEvents.ToArray();
    }
}
