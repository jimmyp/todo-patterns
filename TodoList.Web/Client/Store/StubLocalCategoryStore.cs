using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public class StubLocalCategoryStore : ILocalCategoryStore
{
    private static readonly List<CategorySummary> _categories =
    [
        new CategorySummary { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Work",     Color = "#F59E0B", Icon = "work",          Order = 1, TodoCount = 2 },
        new CategorySummary { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Personal", Color = "#8B5CF6", Icon = "person",         Order = 2, TodoCount = 1 },
        new CategorySummary { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "Urgent",   Color = "#EF4444", Icon = "priority_high",  Order = 3, TodoCount = 0 },
        new CategorySummary { Id = Guid.Parse("00000000-0000-0000-0000-000000000004"), Name = "Design",   Color = "#0EA5E9", Icon = "palette",        Order = 4, TodoCount = 0 },
    ];

    public IReadOnlyList<CategorySummary> Categories => _categories.AsReadOnly();
    public CategorySummary? GetById(Guid id) => _categories.FirstOrDefault(c => c.Id == id);
    public event Action OnChange = delegate { };
}
