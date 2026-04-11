// TodoList.Web/Client/Store/LocalTodoStore.cs
using TodoList.Domain.Projectors;
using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public class LocalTodoStore : ILocalTodoStore
{
    private readonly IClientStore _clientStore;
    private List<TodoSummary> _todos = [];

    public event Action OnChange = delegate { };

    public LocalTodoStore(IClientStore clientStore)
    {
        _clientStore = clientStore;
        _clientStore.OnAggregateChanged += _ => Rebuild();
    }

    public IReadOnlyList<TodoSummary> Todos => _todos.AsReadOnly();
    public TodoSummary? GetById(Guid id) => _todos.FirstOrDefault(t => t.Id == id);

    private void Rebuild()
    {
        // Full rebuild from all events — projector handles ordering
        var allEvents = _clientStore.GetAllEvents();
        _todos = TodoProjector.ProjectAll(allEvents
            .Select(e => new DomainEventEnvelope(e.AggregateId, e.AggregateVersion, e.Type, e.Payload))
            .ToList());
        OnChange();
    }

    public void RebuildAll() => Rebuild();
}
