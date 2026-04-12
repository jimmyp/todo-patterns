// TodoList.Web/Client/Services/StartupSeedService.cs
using System.Net.Http.Json;
using System.Text.Json;
using TodoList.Web.Client.Store;

namespace TodoList.Web.Client.Services;

public class StartupSeedService
{
    private readonly HttpClient _http;
    private readonly IClientStore _store;

    public StartupSeedService(HttpClient http, IClientStore store)
    {
        _http = http;
        _store = store;
    }

    /// <summary>
    /// Seeds ClientStore with confirmed server events from GET /todos and GET /categories.
    /// Skips if store already has confirmed events (not first load).
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var existingConfirmed = _store.GetAllEvents()
            .Any(e => e.State == EventState.Confirmed);

        if (existingConfirmed) return; // Already seeded

        try
        {
            await SeedTodosAsync(ct);
            await SeedCategoriesAsync(ct);
        }
        catch (HttpRequestException)
        {
            // Offline at startup — operate from existing localStorage state
        }
    }

    private async Task SeedTodosAsync(CancellationToken ct)
    {
        var todos = await _http.GetFromJsonAsync<JsonElement[]>("/todos", ct);
        if (todos is null) return;

        foreach (var todo in todos)
        {
            var id = todo.GetProperty("id").GetString() ?? "";
            var evt = new ClientEvent
            {
                Id = Guid.NewGuid().ToString(),
                AggregateId = id,
                AggregateVersion = 0, // Seed events use version 0 — treated as snapshot
                Type = "TodoSeeded",
                Payload = todo,
                Timestamp = DateTimeOffset.UtcNow,
                Source = EventSource.Server,
                State = EventState.Confirmed
            };
            _store.AppendEvent(evt);
        }
    }

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        var categories = await _http.GetFromJsonAsync<JsonElement[]>("/categories", ct);
        if (categories is null) return;

        foreach (var cat in categories)
        {
            var id = cat.GetProperty("id").GetString() ?? "";
            var evt = new ClientEvent
            {
                Id = Guid.NewGuid().ToString(),
                AggregateId = id,
                AggregateVersion = 0,
                Type = "CategorySeeded",
                Payload = cat,
                Timestamp = DateTimeOffset.UtcNow,
                Source = EventSource.Server,
                State = EventState.Confirmed
            };
            _store.AppendEvent(evt);
        }
    }
}
