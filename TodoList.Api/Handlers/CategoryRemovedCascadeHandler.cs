// TodoList.Api/Handlers/CategoryRemovedCascadeHandler.cs
using TodoList.Api.Data;
using TodoList.Domain;
using TodoList.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.Handlers;

/// <summary>
/// Subscribes to UserScopedEvent and reacts when the inner event is a CategoryRemovedEvent.
/// Unassigns the removed category from all todos that reference it. Each unassignment
/// produces a TodoCategoryUnassignedEvent wrapped with that todo's post-mutation version
/// so the projection handler + SignalR push carry the right AggregateId/AggregateVersion.
/// </summary>
public static class CategoryRemovedCascadeHandler
{
    public static async Task<object[]> Handle(UserScopedEvent envelope, ITodoRepository repo, TodoDbContext db)
    {
        if (envelope.Event is not CategoryRemovedEvent evt) return [];

        var todos = await db.Todos
            .Where(t => t.CategoryId == evt.CategoryId && !t.IsDeleted)
            .ToListAsync();

        var cascadedEvents = new List<object>();
        foreach (var todo in todos)
        {
            var result = todo.UnassignCategory();
            if (!result.IsSuccess) continue;

            // Save here so todo.Version reflects the post-mutation value before we
            // wrap the event. Each todo is its own aggregate.
            await repo.SaveAsync();
            foreach (var domainEvt in result.Value!)
                cascadedEvents.Add(new UserScopedEvent(envelope.UserId, todo.Id.ToString(), todo.Version, domainEvt));
        }

        return cascadedEvents.ToArray();
    }
}
