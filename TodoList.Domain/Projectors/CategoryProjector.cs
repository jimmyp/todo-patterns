// TodoList.Domain/Projectors/CategoryProjector.cs
using System.Text.Json;
using TodoList.Domain.ReadModels;

namespace TodoList.Domain.Projectors;

/// <summary>
/// Projects a stream of client-side event envelopes into CategorySummary read models.
/// Handles both domain events (e.g. CategoryAdded) and seed snapshots (CategorySeeded).
/// </summary>
public static class CategoryProjector
{
    /// <summary>
    /// Projects into a list-level summary including the aggregate version (taken from
    /// the highest <see cref="DomainEventEnvelope.AggregateVersion"/> across events).
    /// Callers needing optimistic-concurrency should use this over <see cref="ProjectAll"/>.
    /// </summary>
    public static CategoryListSummary ProjectList(IReadOnlyList<DomainEventEnvelope> events)
    {
        var categories = ProjectAll(events);
        var version = events.Count > 0 ? events.Max(e => e.AggregateVersion) : 0;
        return new CategoryListSummary { Version = version, Categories = categories };
    }

    public static List<CategorySummary> ProjectAll(IReadOnlyList<DomainEventEnvelope> events)
    {
        var state = new Dictionary<Guid, CategorySummary>();

        // CategoryList is a single aggregate — all category events are on one aggregate
        // Each category is identified by categoryId in the payload
        foreach (var evt in events.OrderBy(e => e.AggregateId).ThenBy(e => e.AggregateVersion))
        {
            switch (evt.Type)
            {
                case "CategorySeeded":
                    if (evt.Payload is { } seedPayload)
                    {
                        var seeded = ProjectSeed(seedPayload);
                        if (seeded is not null)
                            state[seeded.Id] = seeded;
                    }
                    break;

                case "CategoryAdded":
                    if (evt.Payload is { } addedPayload)
                    {
                        var added = ProjectAdded(addedPayload);
                        if (added is not null)
                            state[added.Id] = added;
                    }
                    break;

                case "CategoryRenamed":
                    if (evt.Payload is { } renamedPayload)
                    {
                        var catId = GetCategoryId(renamedPayload);
                        if (catId.HasValue && state.TryGetValue(catId.Value, out var toRename))
                        {
                            var newName = renamedPayload.TryGetProperty("newName", out var nn) ? nn.GetString() ?? toRename.Name : toRename.Name;
                            state[catId.Value] = toRename with { Name = newName };
                        }
                    }
                    break;

                case "CategoryColorChanged":
                    if (evt.Payload is { } colorPayload)
                    {
                        var catId = GetCategoryId(colorPayload);
                        if (catId.HasValue && state.TryGetValue(catId.Value, out var toColor))
                        {
                            var newColor = colorPayload.TryGetProperty("newColor", out var nc) ? nc.GetString() ?? toColor.Color : toColor.Color;
                            state[catId.Value] = toColor with { Color = newColor };
                        }
                    }
                    break;

                case "CategoryIconChanged":
                    if (evt.Payload is { } iconPayload)
                    {
                        var catId = GetCategoryId(iconPayload);
                        if (catId.HasValue && state.TryGetValue(catId.Value, out var toIcon))
                        {
                            var newIcon = iconPayload.TryGetProperty("newIcon", out var ni) ? ni.GetString() ?? toIcon.Icon : toIcon.Icon;
                            state[catId.Value] = toIcon with { Icon = newIcon };
                        }
                    }
                    break;

                case "CategoryReordered":
                    if (evt.Payload is { } reorderPayload)
                    {
                        var catId = GetCategoryId(reorderPayload);
                        if (catId.HasValue && state.TryGetValue(catId.Value, out var toReorder))
                        {
                            var newOrder = reorderPayload.TryGetProperty("newOrder", out var no) ? no.GetInt32() : toReorder.Order;
                            state[catId.Value] = toReorder with { Order = newOrder };
                        }
                    }
                    break;

                case "CategoryRemoved":
                    if (evt.Payload is { } removedPayload)
                    {
                        var catId = GetCategoryId(removedPayload);
                        if (catId.HasValue)
                            state.Remove(catId.Value);
                    }
                    break;
            }
        }

        return state.Values.ToList();
    }

    private static Guid? GetCategoryId(JsonElement payload)
    {
        if (payload.TryGetProperty("categoryId", out var cid) && Guid.TryParse(cid.GetString(), out var guid))
            return guid;
        return null;
    }

    private static CategorySummary? ProjectSeed(JsonElement payload)
    {
        if (!payload.TryGetProperty("id", out var idProp)) return null;
        if (!Guid.TryParse(idProp.GetString(), out var id)) return null;

        return new CategorySummary
        {
            Id = id,
            Name = payload.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Color = payload.TryGetProperty("color", out var c) ? c.GetString() ?? "" : "",
            Icon = payload.TryGetProperty("icon", out var ico) ? ico.GetString() ?? "" : "",
            Order = payload.TryGetProperty("order", out var o) ? o.GetInt32() : 0,
            TodoCount = payload.TryGetProperty("todoCount", out var tc) ? tc.GetInt32() : 0
        };
    }

    private static CategorySummary? ProjectAdded(JsonElement payload)
    {
        if (!payload.TryGetProperty("categoryId", out var idProp)) return null;
        if (!Guid.TryParse(idProp.GetString(), out var id)) return null;

        return new CategorySummary
        {
            Id = id,
            Name = payload.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Color = payload.TryGetProperty("color", out var c) ? c.GetString() ?? "" : "",
            Icon = payload.TryGetProperty("icon", out var ico) ? ico.GetString() ?? "" : "",
            Order = payload.TryGetProperty("order", out var o) ? o.GetInt32() : 0,
            TodoCount = 0
        };
    }
}
