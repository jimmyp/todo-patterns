// TodoList.Web/Client/Store/LocalCategoryStore.cs
using TodoList.Domain.Projectors;
using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public class LocalCategoryStore : ILocalCategoryStore
{
    private readonly IClientStore _clientStore;
    private List<CategorySummary> _categories = [];

    public event Action OnChange = delegate { };

    public LocalCategoryStore(IClientStore clientStore)
    {
        _clientStore = clientStore;
        _clientStore.OnAggregateChanged += _ => Rebuild();
    }

    public IReadOnlyList<CategorySummary> Categories => _categories.AsReadOnly();
    public CategorySummary? GetById(Guid id) => _categories.FirstOrDefault(c => c.Id == id);

    private void Rebuild()
    {
        var allEvents = _clientStore.GetAllEvents();
        _categories = CategoryProjector.ProjectAll(allEvents
            .Select(e => new DomainEventEnvelope(e.AggregateId, e.AggregateVersion, e.Type, e.Payload))
            .ToList());
        OnChange();
    }

    public void RebuildAll() => Rebuild();
}
