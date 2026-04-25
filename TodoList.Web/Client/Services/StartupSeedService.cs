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
        // GET /todos returns a flat array — each todo is its own aggregate, so it
        // carries its own `version` which becomes the seed event's AggregateVersion.
        // Subsequent client mutations build on this version; the server uses it for
        // optimistic-concurrency checks via X-Expected-Version.
        var todos = await _http.GetFromJsonAsync<JsonElement[]>("/todos", ct);
        if (todos is null) return;

        foreach (var todo in todos)
        {
            var id = todo.GetProperty("id").GetString() ?? "";
            var version = todo.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32() : 0;
            var evt = new ClientEvent
            {
                Id = Guid.NewGuid().ToString(),
                AggregateId = id,
                AggregateVersion = version,
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
        // GET /categories returns { version, categories: [...] }. The list-level Version
        // is what client commands carry as ExpectedVersion for category mutations
        // (CategoryList is one aggregate per user — synthetic id "user-category-list").
        // We emit one CategorySeeded event per category but key them all on the same
        // aggregate id and use the list version as AggregateVersion so
        // CategoryProjector.ProjectList picks it up via Max(AggregateVersion).
        var response = await _http.GetFromJsonAsync<JsonElement>("/categories", ct);
        if (response.ValueKind != JsonValueKind.Object) return;

        var listVersion = response.TryGetProperty("version", out var lv) && lv.ValueKind == JsonValueKind.Number
            ? lv.GetInt32() : 0;
        if (!response.TryGetProperty("categories", out var categories)) return;

        foreach (var cat in categories.EnumerateArray())
        {
            var evt = new ClientEvent
            {
                Id = Guid.NewGuid().ToString(),
                AggregateId = "user-category-list",
                AggregateVersion = listVersion,
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
