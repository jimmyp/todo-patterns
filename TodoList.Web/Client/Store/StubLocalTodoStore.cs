using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public class StubLocalTodoStore : ILocalTodoStore
{
    private static readonly Guid Cat1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Cat2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly List<TodoSummary> _todos =
    [
        new TodoSummary
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Title = "Review architecture spec",
            IsCompleted = false,
            CategoryId = Cat1,
            CategoryName = "Work",
            CategoryColor = "#F59E0B",
            DueDate = DateTimeOffset.UtcNow.AddDays(1),
            Progress = 60,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            CompletedAt = null
        },
        new TodoSummary
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Title = "Set up dev container",
            IsCompleted = false,
            CategoryId = Cat1,
            CategoryName = "Work",
            CategoryColor = "#F59E0B",
            DueDate = DateTimeOffset.UtcNow.AddDays(-1),
            Progress = 0,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            CompletedAt = null
        },
        new TodoSummary
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Title = "Buy groceries",
            IsCompleted = false,
            CategoryId = Cat2,
            CategoryName = "Personal",
            CategoryColor = "#8B5CF6",
            DueDate = null,
            Progress = 0,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CompletedAt = null
        },
        new TodoSummary
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
            Title = "Update resume",
            IsCompleted = true,
            CategoryId = null,
            CategoryName = null,
            CategoryColor = null,
            DueDate = null,
            Progress = 100,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
            CompletedAt = DateTimeOffset.UtcNow.AddDays(-2)
        },
    ];

    public IReadOnlyList<TodoSummary> Todos => _todos.AsReadOnly();
    public TodoSummary? GetById(Guid id) => _todos.FirstOrDefault(t => t.Id == id);
    public event Action OnChange = delegate { };
}
