// TodoList.Api/EventHandlers/CategoryProjectionHandler.cs
using System.Text.Json;
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
/// Wolverine event handler — subscribes to UserScopedEvent (cascaded from category command handlers)
/// and updates CategorySummary projections. After persistence, pushes a fully-shaped ClientEvent
/// to the originating user's SignalR group so the client can advance its CategoryListSummary
/// version and replace its speculative entry.
/// </summary>
public class CategoryProjectionHandler(TodoDbContext db, IHubContext<EventHub, IEventHubClient> hub)
{
    public async Task Handle(UserScopedEvent envelope)
    {
        var userId = envelope.UserId;
        var evt = envelope.Event;

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
                var renamed = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (renamed is not null)
                {
                    renamed.Name = e.NewName;
                    renamed.Version++;
                    var todos = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
                    await todos.ForEachAsync(t => t.CategoryName = e.NewName);
                }
                break;

            case CategoryColorChangedEvent e:
                var recolored = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (recolored is not null)
                {
                    recolored.Color = e.NewColor;
                    recolored.Version++;
                    var todos = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
                    await todos.ForEachAsync(t => t.CategoryColor = e.NewColor);
                }
                break;

            case CategoryIconChangedEvent e:
                var reicon = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (reicon is not null) { reicon.Icon = e.NewIcon; reicon.Version++; }
                break;

            case CategoryReorderedEvent e:
                var reord = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (reord is not null) { reord.Order = e.NewOrder; reord.Version++; }
                break;

            case CategoryRemovedEvent e:
                var removed = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (removed is not null) db.CategorySummaries.Remove(removed);
                var orphans = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
                await orphans.ForEachAsync(t => { t.CategoryId = null; t.CategoryName = null; t.CategoryColor = null; });
                break;
        }

        await db.SaveChangesAsync();

        // Push the authoritative event to the user's SignalR group. Shape matches client's
        // ClientEvent record. Source=1 (Server), State=1 (Confirmed). The client uses
        // AggregateId ("category-list:{userId}") + AggregateVersion to advance its
        // CategoryListSummary and replace any speculative entry.
        await hub.Clients.Group($"user:{userId}").ReceiveEvent(new
        {
            id = Guid.NewGuid().ToString(),
            aggregateId = envelope.AggregateId,
            aggregateVersion = envelope.AggregateVersion,
            type = evt.GetType().Name.Replace("Event", ""),
            payload = JsonSerializer.SerializeToElement(evt, evt.GetType()),
            timestamp = DateTimeOffset.UtcNow,
            source = 1,
            state = 1
        });
    }
}
