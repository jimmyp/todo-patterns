using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public interface ILocalCategoryStore
{
    IReadOnlyList<CategorySummary> Categories { get; }
    int ListVersion { get; }
    CategorySummary? GetById(Guid id);
    event Action OnChange;
}
