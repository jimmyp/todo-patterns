// TodoList.Api/EventHandlers/TodoProjectionHandler.cs
using Microsoft.AspNetCore.SignalR;
using TodoList.Api.Data;
using TodoList.Api.Data.Projections;
using TodoList.Api.Handlers;
using TodoList.Api.Hubs;
using TodoList.Domain;
using TodoList.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.EventHandlers;

/// <summary>
/// Wolverine event handler — subscribes to UserScopedEvent (cascaded from command handlers)
/// and updates TodoSummary projections. After persistence, pushes the event to the
/// originating user's SignalR group so the client can replace its speculative event.
/// </summary>
public class TodoProjectionHandler(TodoDbContext db, IHubContext<EventHub, IEventHubClient> hub)
{
    public async Task Handle(UserScopedEvent envelope)
    {
        var userId = envelope.UserId;
        var evt = envelope.Event;

        switch (evt)
        {
            case TodoCreatedEvent e:
                db.TodoSummaries.Add(new TodoSummaryEntity
                {
                    Id = e.TodoId,
                    UserId = userId,
                    Title = e.Title,
                    CreatedAt = e.CreatedAt,
                    Version = 1
                });
                break;

            case TodoCompletedEvent e:
                var todo = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo is not null) { todo.IsCompleted = true; todo.CompletedAt = e.CompletedAt; todo.Version++; }
                break;

            case TodoUncompletedEvent e:
                var todo2 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo2 is not null) { todo2.IsCompleted = false; todo2.CompletedAt = null; todo2.Version++; }
                break;

            case TodoDeletedEvent e:
                var todo3 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo3 is not null) db.TodoSummaries.Remove(todo3);
                break;

            case TodoRenamedEvent e:
                var todo4 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo4 is not null) { todo4.Title = e.NewTitle; todo4.Version++; }
                break;

            case TodoCategoryAssignedEvent e:
                var todo5 = await db.TodoSummaries.FindAsync(e.TodoId);
                var cat = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (todo5 is not null)
                {
                    var prevCatId = todo5.CategoryId;
                    todo5.CategoryId = e.CategoryId;
                    todo5.CategoryName = cat?.Name;
                    todo5.CategoryColor = cat?.Color;
                    if (prevCatId.HasValue)
                    {
                        var prev = await db.CategorySummaries.FindAsync(prevCatId.Value);
                        if (prev is not null) prev.TodoCount = Math.Max(0, prev.TodoCount - 1);
                    }
                    if (cat is not null) cat.TodoCount++;
                    todo5.Version++;
                }
                break;

            case TodoCategoryUnassignedEvent e:
                var todo6 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo6 is not null)
                {
                    if (todo6.CategoryId.HasValue)
                    {
                        var prev = await db.CategorySummaries.FindAsync(todo6.CategoryId.Value);
                        if (prev is not null) prev.TodoCount = Math.Max(0, prev.TodoCount - 1);
                    }
                    todo6.CategoryId = null;
                    todo6.CategoryName = null;
                    todo6.CategoryColor = null;
                    todo6.Version++;
                }
                break;

            case TodoDueDateSetEvent e:
                var todo7 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo7 is not null) { todo7.DueDate = e.DueDate; todo7.Version++; }
                break;

            case TodoDueDateClearedEvent e:
                var todo8 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo8 is not null) { todo8.DueDate = null; todo8.Version++; }
                break;

            case TodoNotesUpdatedEvent:
                // Notes not denormalized into TodoSummary — no-op
                break;

            case TodoProgressUpdatedEvent e:
                var todo9 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo9 is not null) { todo9.Progress = e.Progress; todo9.Version++; }
                break;
        }

        await db.SaveChangesAsync();

        // Push the authoritative event to the user's SignalR group so the client can
        // replace its speculative entry. Scoped by UserId to avoid leaking across users.
        await hub.Clients.Group($"user:{userId}").ReceiveEvent(new
        {
            type = evt.GetType().Name.Replace("Event", ""),
            payload = evt
        });
    }
}
