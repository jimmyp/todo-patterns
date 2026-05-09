namespace TodoList.Domain;

/// <summary>
/// Synthetic aggregate ids used on the wire. CategoryList is one aggregate per user
/// — userId scoping comes from the SignalR group / repo, not the id itself.
/// </summary>
public static class AggregateIds
{
    public const string CategoryList = "user-category-list";
}
