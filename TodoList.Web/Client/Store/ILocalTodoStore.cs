using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public interface ILocalTodoStore
{
    IReadOnlyList<TodoSummary> Todos { get; }
    TodoSummary? GetById(Guid id);
    event Action OnChange;
}
