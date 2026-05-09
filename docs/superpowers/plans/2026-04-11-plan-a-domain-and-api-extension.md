# Domain + API Extension Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `TodoList.Domain` shared project, extend `Todo` aggregate with categories/due date/notes/progress, add `CategoryList` aggregate, add projection tables (`TodoSummaries`, `CategorySummaries`), and expose all new API endpoints.

**Architecture:** Domain types move to a new pure-.NET `TodoList.Domain` project (no EF Core, no ASP.NET) referenced by both `TodoList.Api` and the future Blazor WASM client. The API extends its existing EF Core + async-command pattern. GET endpoints serve pre-projected read model tables updated by domain event handlers. All mutating endpoints follow the existing 202+Location pattern and support `X-Expected-Version` header for optimistic concurrency.

**Tech Stack:** .NET 10, EF Core 10, xUnit, FluentAssertions, Testcontainers.MsSql

> **Read before starting:** `TodoList.Api/Domain/Todo.cs`, `TodoList.Api/Domain/DomainResult.cs`, `TodoList.Api/Domain/IDomainEvent.cs`, `TodoList.Api/Domain/Events/TodoEvents.cs`, `TodoList.Api/Data/TodoDbContext.cs`, `TodoList.Api/Endpoints/TodoEndpoints.cs`, `TodoList.Api/Program.cs`, `TodoList.Tests/TodoTests.cs`, `TodoList.IntegrationTests/ApiFixture.cs`

---

## File Map

### New project: `TodoList.Domain/`
```
TodoList.Domain/TodoList.Domain.csproj
TodoList.Domain/DomainResult.cs               # moved from Api
TodoList.Domain/IDomainEvent.cs               # moved from Api
TodoList.Domain/Aggregates/Todo.cs            # moved + extended from Api
TodoList.Domain/Aggregates/CategoryList.cs    # new
TodoList.Domain/Aggregates/Category.cs        # new (value object within CategoryList)
TodoList.Domain/Events/TodoEvents.cs          # moved + extended from Api
TodoList.Domain/Events/CategoryListEvents.cs  # new
TodoList.Domain/Commands/TodoCommands.cs      # new
TodoList.Domain/Commands/CategoryListCommands.cs # new
TodoList.Domain/ReadModels/TodoSummary.cs     # new
TodoList.Domain/ReadModels/CategorySummary.cs # new
TodoList.Domain/Sagas/ISagaDefinition.cs      # new
TodoList.Domain/Projectors/TodoProjector.cs   # new — projects Todo events onto TodoSummary
TodoList.Domain/Projectors/CategoryProjector.cs # new — projects CategoryList events onto CategorySummary
```

### Modified: `TodoList.Api/`
```
TodoList.Api/TodoList.Api.csproj              # add ProjectReference to Domain, remove local Domain files
TodoList.Api/Domain/                          # DELETE entire folder (moved to Domain project)
TodoList.Api/Data/TodoDbContext.cs            # add CategoryList, CategoryListSummary, TodoSummary EF entities
TodoList.Api/Data/CategoryListRepository.cs  # new
TodoList.Api/Data/ICategoryListRepository.cs # new
TodoList.Api/Data/Projections/TodoSummaryProjection.cs     # new — EF entity for TodoSummaries table
TodoList.Api/Data/Projections/CategorySummaryProjection.cs # new — EF entity for CategorySummaries table
TodoList.Api/Endpoints/CategoryEndpoints.cs  # new
TodoList.Api/Endpoints/TodoEndpoints.cs      # extended — new todo sub-endpoints
TodoList.Api/EventHandlers/TodoProjectionHandler.cs        # new — handles Todo events, updates TodoSummaries
TodoList.Api/EventHandlers/CategoryProjectionHandler.cs    # new — handles CategoryList events, updates CategorySummaries
TodoList.Api/Program.cs                      # register new repos + handlers
TodoList.Api/Migrations/                     # new migration: AddCategoriesAndExtendTodos
```

### Modified: `TodoList.Tests/`
```
TodoList.Tests/TodoList.Tests.csproj         # update ProjectReference to TodoList.Domain
TodoList.Tests/GlobalUsings.cs               # update using aliases
TodoList.Tests/TodoTests.cs                  # update namespace references
TodoList.Tests/CategoryListTests.cs          # new
```

### Modified: `TodoList.IntegrationTests/`
```
TodoList.IntegrationTests/Categories/CategoryEndpointTests.cs  # new
TodoList.IntegrationTests/Todos/TodoExtendedEndpointTests.cs   # new
```

---

## Tasks

### Task 1: Create `TodoList.Domain` project

**Files:**
- Create: `TodoList.Domain/TodoList.Domain.csproj`
- Create: `TodoList.Domain/DomainResult.cs`
- Create: `TodoList.Domain/IDomainEvent.cs`

- [ ] **Step 1: Create project**

```bash
cd /Users/jim/code/todo-patterns
dotnet new classlib -n TodoList.Domain -f net10.0 -o TodoList.Domain
rm TodoList.Domain/Class1.cs
dotnet sln add TodoList.Domain/TodoList.Domain.csproj
```

- [ ] **Step 2: Verify .csproj**

Open `TodoList.Domain/TodoList.Domain.csproj` — it should look like:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

No server-side packages. If any were added, remove them.

- [ ] **Step 3: Create `DomainResult.cs`**

```csharp
// TodoList.Domain/DomainResult.cs
namespace TodoList.Domain;

public sealed class DomainResult<T>
{
    private DomainResult(T value)         { Value = value; Errors = []; }
    private DomainResult(string[] errors) { Value = default; Errors = errors; }

    public T? Value { get; }
    public string[] Errors { get; }
    public bool IsSuccess => Errors.Length == 0;

    public static DomainResult<T> Ok(T value)                  => new(value);
    public static DomainResult<T> Fail(params string[] errors) => new(errors);
}
```

- [ ] **Step 4: Create `IDomainEvent.cs`**

```csharp
// TodoList.Domain/IDomainEvent.cs
namespace TodoList.Domain;

public interface IDomainEvent { }
```

- [ ] **Step 5: Build**

```bash
dotnet build TodoList.Domain/TodoList.Domain.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add TodoList.Domain/ TodoList.sln
git commit -m "feat: add TodoList.Domain shared project"
```

---

### Task 2: Move existing domain types to `TodoList.Domain`

**Files:**
- Create: `TodoList.Domain/Aggregates/Todo.cs`
- Create: `TodoList.Domain/Events/TodoEvents.cs`
- Modify: `TodoList.Api/TodoList.Api.csproj`
- Modify: `TodoList.Api/Domain/Todo.cs` (delete)
- Modify: `TodoList.Api/Domain/DomainResult.cs` (delete)
- Modify: `TodoList.Api/Domain/IDomainEvent.cs` (delete)
- Modify: `TodoList.Api/Domain/Events/TodoEvents.cs` (delete)

- [ ] **Step 1: Create `TodoList.Domain/Aggregates/Todo.cs`**

```csharp
// TodoList.Domain/Aggregates/Todo.cs
namespace TodoList.Domain.Aggregates;

public class Todo
{
    private Todo() { }  // EF Core requires parameterless constructor

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public bool IsCompleted { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? CategoryId { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public string? Notes { get; private set; }
    public int Progress { get; private set; }

    public static DomainResult<(Todo todo, IReadOnlyList<IDomainEvent> events)> Create(
        string title, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(title)) errors.Add("Title cannot be empty");
        if (title?.Length > 500)             errors.Add("Title cannot exceed 500 characters");
        if (errors.Count > 0)
            return DomainResult<(Todo, IReadOnlyList<IDomainEvent>)>.Fail([..errors]);

        var todo = new Todo
        {
            Id        = Guid.NewGuid(),
            Title     = title!.Trim(),
            CreatedAt = now
        };

        return DomainResult<(Todo, IReadOnlyList<IDomainEvent>)>.Ok(
            (todo, [new TodoCreatedEvent(todo.Id, todo.Title, now)]));
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Complete(DateTimeOffset now)
    {
        var errors = new List<string>();
        if (IsDeleted)   errors.Add("Cannot complete a deleted todo");
        if (IsCompleted) errors.Add("Already completed");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        IsCompleted = true;
        CompletedAt = now;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoCompletedEvent(Id, now)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Uncomplete()
    {
        var errors = new List<string>();
        if (IsDeleted)    errors.Add("Cannot uncomplete a deleted todo");
        if (!IsCompleted) errors.Add("Not completed");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        IsCompleted = false;
        CompletedAt = null;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoUncompletedEvent(Id)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Already deleted");

        IsDeleted = true;
        DeletedAt = now;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoDeletedEvent(Id, now)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> Rename(string newTitle)
    {
        var errors = new List<string>();
        if (IsDeleted) errors.Add("Cannot rename a deleted todo");
        if (string.IsNullOrWhiteSpace(newTitle)) errors.Add("Title cannot be empty");
        if (newTitle?.Length > 500) errors.Add("Title cannot exceed 500 characters");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        Title = newTitle!.Trim();
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoRenamedEvent(Id, Title)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> AssignCategory(Guid categoryId)
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Cannot update a deleted todo");

        CategoryId = categoryId;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoCategoryAssignedEvent(Id, categoryId)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> UnassignCategory()
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Cannot update a deleted todo");

        CategoryId = null;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoCategoryUnassignedEvent(Id)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> SetDueDate(DateTimeOffset dueDate)
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Cannot update a deleted todo");

        DueDate = dueDate;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoDueDateSetEvent(Id, dueDate)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> ClearDueDate()
    {
        if (IsDeleted)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Cannot update a deleted todo");

        DueDate = null;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoDueDateClearedEvent(Id)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> UpdateNotes(string? notes)
    {
        var errors = new List<string>();
        if (IsDeleted) errors.Add("Cannot update a deleted todo");
        if (notes?.Length > 2000) errors.Add("Notes cannot exceed 2000 characters");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        Notes = notes;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoNotesUpdatedEvent(Id, notes)]);
    }

    public DomainResult<IReadOnlyList<IDomainEvent>> UpdateProgress(int progress)
    {
        var errors = new List<string>();
        if (IsDeleted) errors.Add("Cannot update a deleted todo");
        if (progress < 0 || progress > 100) errors.Add("Progress must be between 0 and 100");
        if (errors.Count > 0)
            return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

        Progress = progress;
        return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoProgressUpdatedEvent(Id, progress)]);
    }
}
```

- [ ] **Step 2: Create `TodoList.Domain/Events/TodoEvents.cs`**

```csharp
// TodoList.Domain/Events/TodoEvents.cs
namespace TodoList.Domain.Events;

public record TodoCreatedEvent(Guid TodoId, string Title, DateTimeOffset CreatedAt) : IDomainEvent;
public record TodoCompletedEvent(Guid TodoId, DateTimeOffset CompletedAt) : IDomainEvent;
public record TodoUncompletedEvent(Guid TodoId) : IDomainEvent;
public record TodoDeletedEvent(Guid TodoId, DateTimeOffset DeletedAt) : IDomainEvent;
public record TodoRenamedEvent(Guid TodoId, string NewTitle) : IDomainEvent;
public record TodoCategoryAssignedEvent(Guid TodoId, Guid CategoryId) : IDomainEvent;
public record TodoCategoryUnassignedEvent(Guid TodoId) : IDomainEvent;
public record TodoDueDateSetEvent(Guid TodoId, DateTimeOffset DueDate) : IDomainEvent;
public record TodoDueDateClearedEvent(Guid TodoId) : IDomainEvent;
public record TodoNotesUpdatedEvent(Guid TodoId, string? Notes) : IDomainEvent;
public record TodoProgressUpdatedEvent(Guid TodoId, int Progress) : IDomainEvent;
```

- [ ] **Step 3: Add global usings to Domain project**

Create `TodoList.Domain/GlobalUsings.cs`:

```csharp
global using TodoList.Domain;
global using TodoList.Domain.Events;
global using TodoList.Domain.Aggregates;
```

- [ ] **Step 4: Add ProjectReference to Api**

Edit `TodoList.Api/TodoList.Api.csproj` — add before `</Project>`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\TodoList.Domain\TodoList.Domain.csproj" />
  </ItemGroup>
```

- [ ] **Step 5: Delete old Api domain files**

```bash
rm /Users/jim/code/todo-patterns/TodoList.Api/Domain/DomainResult.cs
rm /Users/jim/code/todo-patterns/TodoList.Api/Domain/IDomainEvent.cs
rm /Users/jim/code/todo-patterns/TodoList.Api/Domain/Todo.cs
rm /Users/jim/code/todo-patterns/TodoList.Api/Domain/Events/TodoEvents.cs
rmdir /Users/jim/code/todo-patterns/TodoList.Api/Domain/Events
rmdir /Users/jim/code/todo-patterns/TodoList.Api/Domain
```

- [ ] **Step 6: Fix namespace references in Api**

In `TodoList.Api/Data/TodoDbContext.cs`, replace:
```csharp
using TodoList.Api.Domain;
```
with:
```csharp
using TodoList.Domain.Aggregates;
```

In `TodoList.Api/Endpoints/TodoEndpoints.cs`, replace all `TodoList.Api.Domain` references with `TodoList.Domain.Aggregates` and `TodoList.Domain.Events`.

In `TodoList.Api/Data/TodoRepository.cs`, replace `TodoList.Api.Domain` with `TodoList.Domain.Aggregates`.

- [ ] **Step 7: Fix namespace references in Tests**

In `TodoList.Tests/TodoList.Tests.csproj`, replace `ProjectReference` for `TodoList.Api` with references to both:

```xml
  <ItemGroup>
    <ProjectReference Include="..\TodoList.Api\TodoList.Api.csproj" />
    <ProjectReference Include="..\TodoList.Domain\TodoList.Domain.csproj" />
  </ItemGroup>
```

In `TodoList.Tests/GlobalUsings.cs`, replace `TodoList.Api.Domain` with `TodoList.Domain` and `TodoList.Domain.Aggregates`.

- [ ] **Step 8: Build and run existing tests**

```bash
dotnet build TodoList.sln
dotnet test TodoList.Tests/TodoList.Tests.csproj
```

Expected: All 17 tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: move domain types to TodoList.Domain shared project"
```

---

### Task 3: Add `CategoryList` aggregate

**Files:**
- Create: `TodoList.Domain/Aggregates/CategoryList.cs`
- Create: `TodoList.Domain/Aggregates/Category.cs`
- Create: `TodoList.Domain/Events/CategoryListEvents.cs`
- Create: `TodoList.Tests/CategoryListTests.cs`

- [ ] **Step 1: Write failing tests**

Create `TodoList.Tests/CategoryListTests.cs`:

```csharp
using TodoList.Domain.Aggregates;
using TodoList.Domain.Events;
using FluentAssertions;

namespace TodoList.Tests;

public class CategoryListTests
{
    [Fact]
    public void Create_seeds_four_default_categories()
    {
        var (list, events) = CategoryList.Create("user-1");

        list.Categories.Should().HaveCount(4);
        events.Should().HaveCount(4);
        list.Categories.Select(c => c.Name).Should()
            .BeEquivalentTo(["Personal", "Work", "Urgent", "Design"]);
    }

    [Fact]
    public void AddCategory_succeeds_with_unique_name()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory("Hobby", "#FF0000", "star");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Hobby");
        list.Categories.Should().HaveCount(5);
    }

    [Fact]
    public void AddCategory_fails_when_name_already_exists()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory("Personal", "#FF0000", "star");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("already exists"));
    }

    [Fact]
    public void AddCategory_fails_when_name_is_empty()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory("", "#FF0000", "star");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("required"));
    }

    [Fact]
    public void AddCategory_fails_when_name_exceeds_50_chars()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory(new string('x', 51), "#FF0000", "star");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("50"));
    }

    [Fact]
    public void AddCategory_fails_when_color_is_invalid()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory("Hobby", "notacolor", "star");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("color"));
    }

    [Fact]
    public void RenameCategory_succeeds()
    {
        var (list, _) = CategoryList.Create("user-1");
        var personal = list.Categories.First(c => c.Name == "Personal");
        var result = list.RenameCategory(personal.Id, "Home");

        result.IsSuccess.Should().BeTrue();
        result.Value!.NewName.Should().Be("Home");
        list.Categories.First(c => c.Id == personal.Id).Name.Should().Be("Home");
    }

    [Fact]
    public void RenameCategory_fails_when_new_name_conflicts()
    {
        var (list, _) = CategoryList.Create("user-1");
        var personal = list.Categories.First(c => c.Name == "Personal");
        var result = list.RenameCategory(personal.Id, "Work");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("already exists"));
    }

    [Fact]
    public void RemoveCategory_succeeds()
    {
        var (list, _) = CategoryList.Create("user-1");
        var personal = list.Categories.First(c => c.Name == "Personal");
        var result = list.RemoveCategory(personal.Id);

        result.IsSuccess.Should().BeTrue();
        list.Categories.Should().HaveCount(3);
    }

    [Fact]
    public void RemoveCategory_fails_when_not_found()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.RemoveCategory(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test TodoList.Tests/TodoList.Tests.csproj --filter "CategoryListTests"
```

Expected: Compilation errors — `CategoryList` not found.

- [ ] **Step 3: Create `Category.cs`**

```csharp
// TodoList.Domain/Aggregates/Category.cs
namespace TodoList.Domain.Aggregates;

public record Category(
    Guid Id,
    string Name,
    string Color,
    string Icon,
    int Order,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 4: Create `CategoryListEvents.cs`**

```csharp
// TodoList.Domain/Events/CategoryListEvents.cs
namespace TodoList.Domain.Events;

public record CategoryAddedEvent(string UserId, Guid CategoryId, string Name, string Color, string Icon, int Order) : IDomainEvent;
public record CategoryRenamedEvent(string UserId, Guid CategoryId, string NewName) : IDomainEvent;
public record CategoryColorChangedEvent(string UserId, Guid CategoryId, string NewColor) : IDomainEvent;
public record CategoryIconChangedEvent(string UserId, Guid CategoryId, string NewIcon) : IDomainEvent;
public record CategoryReorderedEvent(string UserId, Guid CategoryId, int NewOrder) : IDomainEvent;
public record CategoryRemovedEvent(string UserId, Guid CategoryId) : IDomainEvent;
```

- [ ] **Step 5: Create `CategoryList.cs`**

```csharp
// TodoList.Domain/Aggregates/CategoryList.cs
using System.Text.RegularExpressions;

namespace TodoList.Domain.Aggregates;

public class CategoryList
{
    private readonly List<Category> _categories = [];

    private CategoryList() { }

    public string UserId { get; private set; } = "";
    public int Version { get; private set; }
    public IReadOnlyList<Category> Categories => _categories.AsReadOnly();

    private static readonly (string Name, string Color, string Icon)[] Defaults =
    [
        ("Personal", "#8B5CF6", "person"),
        ("Work",     "#F59E0B", "work"),
        ("Urgent",   "#EF4444", "priority_high"),
        ("Design",   "#0EA5E9", "palette"),
    ];

    public static (CategoryList list, IReadOnlyList<CategoryAddedEvent> events) Create(string userId)
    {
        var list = new CategoryList { UserId = userId };
        var events = new List<CategoryAddedEvent>();
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < Defaults.Length; i++)
        {
            var (name, color, icon) = Defaults[i];
            var id = Guid.NewGuid();
            var category = new Category(id, name, color, icon, i, now);
            list._categories.Add(category);
            events.Add(new CategoryAddedEvent(userId, id, name, color, icon, i));
        }

        return (list, events);
    }

    public DomainResult<CategoryAddedEvent> AddCategory(string name, string color, string icon)
    {
        var errors = Validate(name, color, icon);
        if (errors.Count > 0) return DomainResult<CategoryAddedEvent>.Fail([..errors]);

        if (_categories.Any(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return DomainResult<CategoryAddedEvent>.Fail("Category name already exists");

        var id = Guid.NewGuid();
        var order = _categories.Count;
        var category = new Category(id, name.Trim(), color, icon, order, DateTimeOffset.UtcNow);
        _categories.Add(category);

        return DomainResult<CategoryAddedEvent>.Ok(
            new CategoryAddedEvent(UserId, id, category.Name, color, icon, order));
    }

    public DomainResult<CategoryRenamedEvent> RenameCategory(Guid id, string newName)
    {
        var category = _categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
            return DomainResult<CategoryRenamedEvent>.Fail("Category not found");

        if (string.IsNullOrWhiteSpace(newName))
            return DomainResult<CategoryRenamedEvent>.Fail("Name is required");

        if (newName.Length > 50)
            return DomainResult<CategoryRenamedEvent>.Fail("Name cannot exceed 50 characters");

        if (_categories.Any(c => c.Id != id && c.Name.Equals(newName.Trim(), StringComparison.OrdinalIgnoreCase)))
            return DomainResult<CategoryRenamedEvent>.Fail("Category name already exists");

        var updated = category with { Name = newName.Trim() };
        _categories[_categories.IndexOf(category)] = updated;
        Version++;

        return DomainResult<CategoryRenamedEvent>.Ok(new CategoryRenamedEvent(UserId, id, updated.Name));
    }

    public DomainResult<CategoryColorChangedEvent> ChangeColor(Guid id, string color)
    {
        var category = _categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
            return DomainResult<CategoryColorChangedEvent>.Fail("Category not found");

        if (!IsValidColor(color))
            return DomainResult<CategoryColorChangedEvent>.Fail("Invalid color — must be a hex color e.g. #FF0000");

        var updated = category with { Color = color };
        _categories[_categories.IndexOf(category)] = updated;
        Version++;

        return DomainResult<CategoryColorChangedEvent>.Ok(new CategoryColorChangedEvent(UserId, id, color));
    }

    public DomainResult<CategoryIconChangedEvent> ChangeIcon(Guid id, string icon)
    {
        var category = _categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
            return DomainResult<CategoryIconChangedEvent>.Fail("Category not found");

        if (string.IsNullOrWhiteSpace(icon) || icon.Length > 50)
            return DomainResult<CategoryIconChangedEvent>.Fail("Icon is required and cannot exceed 50 characters");

        var updated = category with { Icon = icon };
        _categories[_categories.IndexOf(category)] = updated;
        Version++;

        return DomainResult<CategoryIconChangedEvent>.Ok(new CategoryIconChangedEvent(UserId, id, icon));
    }

    public DomainResult<CategoryReorderedEvent> Reorder(Guid id, int newOrder)
    {
        var category = _categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
            return DomainResult<CategoryReorderedEvent>.Fail("Category not found");

        var updated = category with { Order = newOrder };
        _categories[_categories.IndexOf(category)] = updated;
        Version++;

        return DomainResult<CategoryReorderedEvent>.Ok(new CategoryReorderedEvent(UserId, id, newOrder));
    }

    public DomainResult<CategoryRemovedEvent> RemoveCategory(Guid id)
    {
        var category = _categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
            return DomainResult<CategoryRemovedEvent>.Fail("Category not found");

        _categories.Remove(category);
        Version++;

        return DomainResult<CategoryRemovedEvent>.Ok(new CategoryRemovedEvent(UserId, id));
    }

    private static List<string> Validate(string name, string color, string icon)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name)) errors.Add("Name is required");
        if (name?.Length > 50) errors.Add("Name cannot exceed 50 characters");
        if (!IsValidColor(color)) errors.Add("Invalid color — must be a hex color e.g. #FF0000");
        if (string.IsNullOrWhiteSpace(icon) || icon.Length > 50) errors.Add("Icon is required and cannot exceed 50 characters");
        return errors;
    }

    private static bool IsValidColor(string color) =>
        Regex.IsMatch(color ?? "", @"^#[0-9A-Fa-f]{6}$");
}
```

- [ ] **Step 6: Run tests — verify they pass**

```bash
dotnet test TodoList.Tests/TodoList.Tests.csproj --filter "CategoryListTests"
```

Expected: 10 tests pass.

- [ ] **Step 7: Run all tests**

```bash
dotnet test TodoList.Tests/TodoList.Tests.csproj
```

Expected: All 27 tests pass.

- [ ] **Step 8: Commit**

```bash
git add TodoList.Domain/ TodoList.Tests/
git commit -m "feat: add CategoryList aggregate with seed categories"
```

---

### Task 4: Add read models and `ISagaDefinition`

**Files:**
- Create: `TodoList.Domain/ReadModels/TodoSummary.cs`
- Create: `TodoList.Domain/ReadModels/CategorySummary.cs`
- Create: `TodoList.Domain/Sagas/ISagaDefinition.cs`
- Create: `TodoList.Domain/Commands/TodoCommands.cs`
- Create: `TodoList.Domain/Commands/CategoryListCommands.cs`

- [ ] **Step 1: Create `TodoSummary.cs`**

```csharp
// TodoList.Domain/ReadModels/TodoSummary.cs
namespace TodoList.Domain.ReadModels;

public record TodoSummary
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public bool IsCompleted { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public string? CategoryColor { get; init; }
    public DateTimeOffset? DueDate { get; init; }
    public bool IsOverdue => DueDate.HasValue && !IsCompleted && DueDate.Value < DateTimeOffset.UtcNow;
    public int Progress { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
```

- [ ] **Step 2: Create `CategorySummary.cs`**

```csharp
// TodoList.Domain/ReadModels/CategorySummary.cs
namespace TodoList.Domain.ReadModels;

public record CategorySummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Color { get; init; } = "";
    public string Icon { get; init; } = "";
    public int Order { get; init; }
    public int TodoCount { get; init; }
}
```

- [ ] **Step 3: Create `ISagaDefinition.cs`**

```csharp
// TodoList.Domain/Sagas/ISagaDefinition.cs
namespace TodoList.Domain.Sagas;

/// <summary>
/// Marker interface for saga definitions. Implementations declare which command type
/// initiates the saga. The Blazor client reflects over these at startup to detect
/// saga-initiating commands and show appropriate offline toasts.
/// </summary>
public interface ISagaDefinition
{
    Type InitiatingCommandType { get; }
    string Description { get; }
}
```

- [ ] **Step 4: Create `TodoCommands.cs`**

```csharp
// TodoList.Domain/Commands/TodoCommands.cs
namespace TodoList.Domain.Commands;

public record CreateTodoCommand(string Title, Guid? CategoryId = null, DateTimeOffset? DueDate = null, string? Notes = null, int Progress = 0);
public record RenameTodoCommand(Guid TodoId, string NewTitle);
public record CompleteTodoCommand(Guid TodoId);
public record UncompleteTodoCommand(Guid TodoId);
public record DeleteTodoCommand(Guid TodoId);
public record AssignCategoryCommand(Guid TodoId, Guid CategoryId);
public record UnassignCategoryCommand(Guid TodoId);
public record SetDueDateCommand(Guid TodoId, DateTimeOffset DueDate);
public record ClearDueDateCommand(Guid TodoId);
public record UpdateNotesCommand(Guid TodoId, string? Notes);
public record UpdateProgressCommand(Guid TodoId, int Progress);
```

- [ ] **Step 5: Create `CategoryListCommands.cs`**

```csharp
// TodoList.Domain/Commands/CategoryListCommands.cs
namespace TodoList.Domain.Commands;

public record AddCategoryCommand(string Name, string Color, string Icon);
public record RenameCategoryCommand(Guid CategoryId, string NewName);
public record ChangeCategoryColorCommand(Guid CategoryId, string Color);
public record ChangeCategoryIconCommand(Guid CategoryId, string Icon);
public record ReorderCategoryCommand(Guid CategoryId, int Order);
public record RemoveCategoryCommand(Guid CategoryId);
```

- [ ] **Step 6: Build**

```bash
dotnet build TodoList.Domain/TodoList.Domain.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add TodoList.Domain/
git commit -m "feat: add read models, commands, ISagaDefinition to TodoList.Domain"
```

---

### Task 5: Add EF entities for projections + CategoryList persistence

**Files:**
- Create: `TodoList.Api/Data/Projections/TodoSummaryEntity.cs`
- Create: `TodoList.Api/Data/Projections/CategorySummaryEntity.cs`
- Create: `TodoList.Api/Data/CategoryListEntity.cs`
- Create: `TodoList.Api/Data/CategoryEntity.cs`
- Modify: `TodoList.Api/Data/TodoDbContext.cs`
- Create: `TodoList.Api/Data/ICategoryListRepository.cs`
- Create: `TodoList.Api/Data/CategoryListRepository.cs`

- [ ] **Step 1: Create `TodoSummaryEntity.cs`**

```csharp
// TodoList.Api/Data/Projections/TodoSummaryEntity.cs
namespace TodoList.Api.Data.Projections;

public class TodoSummaryEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryColor { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public int Progress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

- [ ] **Step 2: Create `CategorySummaryEntity.cs`**

```csharp
// TodoList.Api/Data/Projections/CategorySummaryEntity.cs
namespace TodoList.Api.Data.Projections;

public class CategorySummaryEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Order { get; set; }
    public int TodoCount { get; set; }
}
```

- [ ] **Step 3: Create `CategoryListEntity.cs` and `CategoryEntity.cs`**

```csharp
// TodoList.Api/Data/CategoryListEntity.cs
namespace TodoList.Api.Data;

public class CategoryListEntity
{
    public string UserId { get; set; } = "";
    public int Version { get; set; }
    public List<CategoryEntity> Categories { get; set; } = [];
}

// TodoList.Api/Data/CategoryEntity.cs
namespace TodoList.Api.Data;

public class CategoryEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Order { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 4: Update `TodoDbContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using TodoList.Api.Data.Projections;
using TodoList.Domain.Aggregates;

namespace TodoList.Api.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();
    public DbSet<TodoOperation> Operations => Set<TodoOperation>();
    public DbSet<CategoryListEntity> CategoryLists => Set<CategoryListEntity>();
    public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();
    public DbSet<TodoSummaryEntity> TodoSummaries => Set<TodoSummaryEntity>();
    public DbSet<CategorySummaryEntity> CategorySummaries => Set<CategorySummaryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Title).HasMaxLength(500).IsRequired();
            b.Property(t => t.Notes).HasMaxLength(2000);
            b.HasQueryFilter(t => !t.IsDeleted);
        });

        modelBuilder.Entity<TodoOperation>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Status).HasMaxLength(20).IsRequired();
            b.Property(o => o.FailureReason).HasMaxLength(2000);
        });

        modelBuilder.Entity<CategoryListEntity>(b =>
        {
            b.HasKey(cl => cl.UserId);
            b.HasMany(cl => cl.Categories).WithOne().HasForeignKey(c => c.UserId)
                .HasPrincipalKey(cl => cl.UserId);
        });

        modelBuilder.Entity<CategoryEntity>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(50).IsRequired();
            b.Property(c => c.Color).HasMaxLength(10).IsRequired();
            b.Property(c => c.Icon).HasMaxLength(50).IsRequired();
            b.HasIndex(c => c.UserId);
        });

        modelBuilder.Entity<TodoSummaryEntity>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Title).HasMaxLength(500).IsRequired();
            b.Property(t => t.CategoryName).HasMaxLength(50);
            b.Property(t => t.CategoryColor).HasMaxLength(10);
            b.HasIndex(t => t.UserId);
        });

        modelBuilder.Entity<CategorySummaryEntity>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(50).IsRequired();
            b.Property(c => c.Color).HasMaxLength(10).IsRequired();
            b.Property(c => c.Icon).HasMaxLength(50).IsRequired();
            b.HasIndex(c => c.UserId);
        });
    }
}
```

- [ ] **Step 5: Create `ICategoryListRepository.cs`**

```csharp
// TodoList.Api/Data/ICategoryListRepository.cs
using TodoList.Domain.Aggregates;

namespace TodoList.Api.Data;

public interface ICategoryListRepository
{
    Task<CategoryList?> GetByUserIdAsync(string userId);
    Task AddAsync(CategoryList categoryList);
    Task SaveAsync();
}
```

- [ ] **Step 6: Create `CategoryListRepository.cs`**

```csharp
// TodoList.Api/Data/CategoryListRepository.cs
using TodoList.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.Data;

public class CategoryListRepository(TodoDbContext db) : ICategoryListRepository
{
    public async Task<CategoryList?> GetByUserIdAsync(string userId)
    {
        var entity = await db.CategoryLists
            .Include(cl => cl.Categories)
            .FirstOrDefaultAsync(cl => cl.UserId == userId);

        if (entity is null) return null;

        return CategoryList.Reconstitute(
            entity.UserId,
            entity.Version,
            entity.Categories.Select(c => new Category(c.Id, c.Name, c.Color, c.Icon, c.Order, c.CreatedAt)).ToList());
    }

    public async Task AddAsync(CategoryList categoryList)
    {
        var entity = new CategoryListEntity
        {
            UserId = categoryList.UserId,
            Version = categoryList.Version,
            Categories = categoryList.Categories.Select(c => new CategoryEntity
            {
                Id = c.Id,
                UserId = categoryList.UserId,
                Name = c.Name,
                Color = c.Color,
                Icon = c.Icon,
                Order = c.Order,
                CreatedAt = c.CreatedAt
            }).ToList()
        };
        await db.CategoryLists.AddAsync(entity);
    }

    public Task SaveAsync() => db.SaveChangesAsync().ContinueWith(_ => { });
}
```

- [ ] **Step 7: Add `Reconstitute` method to `CategoryList`**

In `TodoList.Domain/Aggregates/CategoryList.cs`, add after the `Create` method:

```csharp
    /// <summary>Used by the repository to rehydrate a CategoryList from persistence.</summary>
    public static CategoryList Reconstitute(string userId, int version, IReadOnlyList<Category> categories)
    {
        var list = new CategoryList { UserId = userId, Version = version };
        list._categories.AddRange(categories);
        return list;
    }
```

- [ ] **Step 8: Build**

```bash
dotnet build TodoList.Api/TodoList.Api.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add TodoList.Api/Data/ TodoList.Domain/Aggregates/CategoryList.cs
git commit -m "feat: add EF entities for CategoryList and projection tables"
```

---

### Task 6: EF Core migration

**Files:**
- New: `TodoList.Api/Migrations/` (new migration files generated)

- [ ] **Step 1: Generate migration**

```bash
cd /Users/jim/code/todo-patterns
dotnet ef migrations add AddCategoriesAndExtendTodos \
  --project TodoList.Api/TodoList.Api.csproj \
  --startup-project TodoList.Api/TodoList.Api.csproj
```

Expected: New migration file created in `TodoList.Api/Migrations/`.

- [ ] **Step 2: Review migration**

Open the generated migration file and verify it contains:
- New table `CategoryLists` with `UserId` PK and `Version`
- New table `Categories` with `Id`, `UserId`, `Name`, `Color`, `Icon`, `Order`, `CreatedAt`
- New table `TodoSummaries` with all projection columns
- New table `CategorySummaries` with all projection columns
- New columns on `Todos`: `CategoryId` (nullable), `DueDate`, `Notes`, `Progress`

If the migration is missing columns on `Todos` (EF may not pick up private setters without proper configuration), add them manually to the migration `Up()` method:

```csharp
migrationBuilder.AddColumn<Guid>(name: "CategoryId", table: "Todos", nullable: true);
migrationBuilder.AddColumn<DateTimeOffset>(name: "DueDate", table: "Todos", nullable: true);
migrationBuilder.AddColumn<string>(name: "Notes", table: "Todos", maxLength: 2000, nullable: true);
migrationBuilder.AddColumn<int>(name: "Progress", table: "Todos", nullable: false, defaultValue: 0);
```

- [ ] **Step 3: Run integration tests to verify migration applies**

```bash
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj
```

Expected: All existing integration tests pass.

- [ ] **Step 4: Commit**

```bash
git add TodoList.Api/Migrations/
git commit -m "feat: migration AddCategoriesAndExtendTodos"
```

---

### Task 7: Event projection handlers

**Files:**
- Create: `TodoList.Api/EventHandlers/TodoProjectionHandler.cs`
- Create: `TodoList.Api/EventHandlers/CategoryProjectionHandler.cs`
- Modify: `TodoList.Api/Program.cs`

- [ ] **Step 1: Create `TodoProjectionHandler.cs`**

```csharp
// TodoList.Api/EventHandlers/TodoProjectionHandler.cs
using TodoList.Api.Data;
using TodoList.Api.Data.Projections;
using TodoList.Domain;
using TodoList.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.EventHandlers;

public class TodoProjectionHandler(TodoDbContext db)
{
    public async Task HandleAsync(string userId, IDomainEvent evt)
    {
        switch (evt)
        {
            case TodoCreatedEvent e:
                db.TodoSummaries.Add(new TodoSummaryEntity
                {
                    Id = e.TodoId,
                    UserId = userId,
                    Title = e.Title,
                    CreatedAt = e.CreatedAt
                });
                break;

            case TodoCompletedEvent e:
                var todo = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo is not null) { todo.IsCompleted = true; todo.CompletedAt = e.CompletedAt; }
                break;

            case TodoUncompletedEvent e:
                var todo2 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo2 is not null) { todo2.IsCompleted = false; todo2.CompletedAt = null; }
                break;

            case TodoDeletedEvent e:
                var todo3 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo3 is not null) db.TodoSummaries.Remove(todo3);
                break;

            case TodoRenamedEvent e:
                var todo4 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo4 is not null) todo4.Title = e.NewTitle;
                break;

            case TodoCategoryAssignedEvent e:
                var todo5 = await db.TodoSummaries.FindAsync(e.TodoId);
                var cat = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (todo5 is not null)
                {
                    var prevCatId = todo5.CategoryId;
                    todo5.CategoryId = e.CategoryId;
                    todo5.CategoryName = cat?.Name;
                    todo5.CategoryColor = cat?.Color;
                    // Update counts
                    if (prevCatId.HasValue)
                    {
                        var prev = await db.CategorySummaries.FindAsync(prevCatId.Value);
                        if (prev is not null) prev.TodoCount = Math.Max(0, prev.TodoCount - 1);
                    }
                    if (cat is not null) cat.TodoCount++;
                }
                break;

            case TodoCategoryUnassignedEvent e:
                var todo6 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo6 is not null)
                {
                    if (todo6.CategoryId.HasValue)
                    {
                        var prev = await db.CategorySummaries.FindAsync(todo6.CategoryId.Value);
                        if (prev is not null) prev.TodoCount = Math.Max(0, prev.TodoCount - 1);
                    }
                    todo6.CategoryId = null;
                    todo6.CategoryName = null;
                    todo6.CategoryColor = null;
                }
                break;

            case TodoDueDateSetEvent e:
                var todo7 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo7 is not null) todo7.DueDate = e.DueDate;
                break;

            case TodoDueDateClearedEvent e:
                var todo8 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo8 is not null) todo8.DueDate = null;
                break;

            case TodoProgressUpdatedEvent e:
                var todo9 = await db.TodoSummaries.FindAsync(e.TodoId);
                if (todo9 is not null) todo9.Progress = e.Progress;
                break;
        }

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Create `CategoryProjectionHandler.cs`**

```csharp
// TodoList.Api/EventHandlers/CategoryProjectionHandler.cs
using TodoList.Api.Data;
using TodoList.Api.Data.Projections;
using TodoList.Domain;
using TodoList.Domain.Events;

namespace TodoList.Api.EventHandlers;

public class CategoryProjectionHandler(TodoDbContext db)
{
    public async Task HandleAsync(IDomainEvent evt)
    {
        switch (evt)
        {
            case CategoryAddedEvent e:
                db.CategorySummaries.Add(new CategorySummaryEntity
                {
                    Id = e.CategoryId,
                    UserId = e.UserId,
                    Name = e.Name,
                    Color = e.Color,
                    Icon = e.Icon,
                    Order = e.Order,
                    TodoCount = 0
                });
                break;

            case CategoryRenamedEvent e:
                var cat = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat is not null)
                {
                    cat.Name = e.NewName;
                    // Update denormalized name in TodoSummaries
                    var todos = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
                    await todos.ForEachAsync(t => t.CategoryName = e.NewName);
                }
                break;

            case CategoryColorChangedEvent e:
                var cat2 = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat2 is not null)
                {
                    cat2.Color = e.NewColor;
                    var todos = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
                    await todos.ForEachAsync(t => t.CategoryColor = e.NewColor);
                }
                break;

            case CategoryIconChangedEvent e:
                var cat3 = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat3 is not null) cat3.Icon = e.NewIcon;
                break;

            case CategoryReorderedEvent e:
                var cat4 = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat4 is not null) cat4.Order = e.NewOrder;
                break;

            case CategoryRemovedEvent e:
                var cat5 = await db.CategorySummaries.FindAsync(e.CategoryId);
                if (cat5 is not null) db.CategorySummaries.Remove(cat5);
                // Unassign todos from this category
                var todos2 = db.TodoSummaries.Where(t => t.CategoryId == e.CategoryId);
                await todos2.ForEachAsync(t => { t.CategoryId = null; t.CategoryName = null; t.CategoryColor = null; });
                break;
        }

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 3: Register handlers in `Program.cs`**

Add to `Program.cs` before `var app = builder.Build()`:

```csharp
builder.Services.AddScoped<ICategoryListRepository, CategoryListRepository>();
builder.Services.AddScoped<TodoProjectionHandler>();
builder.Services.AddScoped<CategoryProjectionHandler>();
```

- [ ] **Step 4: Build**

```bash
dotnet build TodoList.Api/TodoList.Api.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add TodoList.Api/EventHandlers/ TodoList.Api/Program.cs
git commit -m "feat: add todo and category projection handlers"
```

---

### Task 8: New API endpoints — Categories

**Files:**
- Create: `TodoList.Api/Endpoints/CategoryEndpoints.cs`
- Modify: `TodoList.Api/Program.cs`

- [ ] **Step 1: Write integration test first**

Create `TodoList.IntegrationTests/Categories/CategoryEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TodoList.Domain.ReadModels;

namespace TodoList.IntegrationTests.Categories;

public class CategoryEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetCategories_returns_seeded_categories_for_new_user()
    {
        // Seed categories for test user by calling the API
        var seedResponse = await fixture.Client.PostAsJsonAsync("/categories/seed", new { });
        seedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await fixture.Client.GetAsync("/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var categories = await response.Content.ReadFromJsonAsync<CategorySummary[]>();
        categories.Should().HaveCount(4);
        categories!.Select(c => c.Name).Should().BeEquivalentTo(["Personal", "Work", "Urgent", "Design"]);
    }

    [Fact]
    public async Task PostCategory_returns_202_and_creates_category()
    {
        await fixture.Client.PostAsJsonAsync("/categories/seed", new { });
        var response = await fixture.Client.PostAsJsonAsync("/categories",
            new { name = "Hobby", color = "#FF0000", icon = "star" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task PostCategory_returns_422_for_invalid_color()
    {
        await fixture.Client.PostAsJsonAsync("/categories/seed", new { });
        var response = await fixture.Client.PostAsJsonAsync("/categories",
            new { name = "Hobby", color = "notacolor", icon = "star" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task DeleteCategory_returns_202()
    {
        await fixture.Client.PostAsJsonAsync("/categories/seed", new { });
        var cats = await fixture.Client.GetFromJsonAsync<CategorySummary[]>("/categories");
        var id = cats![0].Id;

        var response = await fixture.Client.DeleteAsync($"/categories/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
```

- [ ] **Step 2: Run test — verify it fails**

```bash
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj \
  --filter "CategoryEndpointTests"
```

Expected: 404 Not Found — endpoints don't exist yet.

- [ ] **Step 3: Create `CategoryEndpoints.cs`**

```csharp
// TodoList.Api/Endpoints/CategoryEndpoints.cs
using TodoList.Api.Data;
using TodoList.Api.Data.Projections;
using TodoList.Api.EventHandlers;
using TodoList.Api.Operations;
using TodoList.Domain.Aggregates;
using TodoList.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.Endpoints;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this WebApplication app)
    {
        app.MapGet("/categories", async (TodoDbContext db, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var categories = await db.CategorySummaries
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Order)
                .ToListAsync();
            return Results.Ok(categories.Select(c => new
            {
                c.Id, c.Name, c.Color, c.Icon, c.Order, c.TodoCount
            }));
        });

        // Seed default categories for user (called on first login)
        app.MapPost("/categories/seed", async (
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            TodoDbContext db,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var existing = await repo.GetByUserIdAsync(userId);
            if (existing is not null) return Results.Ok();

            var (list, events) = CategoryList.Create(userId);
            await repo.AddAsync(list);
            await repo.SaveAsync();

            foreach (var evt in events)
                await projectionHandler.HandleAsync(evt);

            return Results.Ok();
        });

        app.MapPost("/categories", async (
            AddCategoryRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound("CategoryList not found — call /categories/seed first");

            var result = list.AddCategory(request.Name, request.Color, request.Icon);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), result.Value!.CategoryId.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();

            return Results.Accepted(
                $"/todos/operations/{operation.Id}",
                new { operationId = operation.Id });
        }).AddEndpointFilter(async (ctx, next) =>
        {
            var result = await next(ctx);
            return result;
        });

        app.MapPost("/categories/{id}/rename", async (
            Guid id,
            RenameCategoryRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.RenameCategory(id, request.Name);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });

        app.MapPost("/categories/{id}/change-color", async (
            Guid id,
            ChangeCategoryColorRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.ChangeColor(id, request.Color);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });

        app.MapPost("/categories/{id}/change-icon", async (
            Guid id,
            ChangeCategoryIconRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.ChangeIcon(id, request.Icon);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });

        app.MapPost("/categories/{id}/reorder", async (
            Guid id,
            ReorderCategoryRequest request,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.Reorder(id, request.Order);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });

        app.MapDelete("/categories/{id}", async (
            Guid id,
            ICategoryListRepository repo,
            CategoryProjectionHandler projectionHandler,
            IOperationRepository opRepo,
            HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var list = await repo.GetByUserIdAsync(userId);
            if (list is null) return Results.NotFound();

            var result = list.RemoveCategory(id);
            if (!result.IsSuccess)
                return Results.UnprocessableEntity(new { errors = result.Errors });

            await repo.SaveAsync();
            await projectionHandler.HandleAsync(result.Value!);

            var operation = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
            await opRepo.AddAsync(operation);
            await opRepo.SaveAsync();
            return Results.Accepted($"/todos/operations/{operation.Id}", new { operationId = operation.Id });
        });
    }

    private record AddCategoryRequest(string Name, string Color, string Icon);
    private record RenameCategoryRequest(string Name);
    private record ChangeCategoryColorRequest(string Color);
    private record ChangeCategoryIconRequest(string Icon);
    private record ReorderCategoryRequest(int Order);
}
```

- [ ] **Step 4: Add `CreateCompleted` factory to `TodoOperation`**

Read `TodoList.Api/Operations/TodoOperation.cs` and add:

```csharp
public static TodoOperation CreateCompleted(Guid id, string resultJson)
{
    return new TodoOperation
    {
        Id = id,
        Status = "complete",
        ResultJson = resultJson,
        CreatedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };
}
```

- [ ] **Step 5: Register category endpoints in `Program.cs`**

Add `app.MapCategoryEndpoints();` after `app.MapTodoEndpoints();`.

- [ ] **Step 6: Run integration tests**

```bash
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj \
  --filter "CategoryEndpointTests"
```

Expected: 4 tests pass.

- [ ] **Step 7: Commit**

```bash
git add TodoList.Api/Endpoints/CategoryEndpoints.cs \
        TodoList.Api/Operations/TodoOperation.cs \
        TodoList.Api/Program.cs
git commit -m "feat: add category API endpoints"
```

---

### Task 9: Extended Todo endpoints

**Files:**
- Modify: `TodoList.Api/Endpoints/TodoEndpoints.cs`
- Modify: `TodoList.Api/Data/TodoDbContext.cs` (GET /todos now reads TodoSummaries)

- [ ] **Step 1: Write integration tests**

Create `TodoList.IntegrationTests/Todos/TodoExtendedEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace TodoList.IntegrationTests.Todos;

public class TodoExtendedEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task PostTodo_creates_summary_readable_via_get()
    {
        var response = await fixture.Client.PostAsJsonAsync("/todos",
            new { title = "Test task" });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Poll operation
        var location = response.Headers.Location!.ToString();
        var opResponse = await fixture.Client.GetAsync(location);
        opResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var todos = await fixture.Client.GetFromJsonAsync<dynamic[]>("/todos");
        todos.Should().Contain(t => t!.GetProperty("title").GetString() == "Test task");
    }

    [Fact]
    public async Task RenameTodo_updates_title()
    {
        var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "Original" });
        var opId = (await createResponse.Content.ReadFromJsonAsync<dynamic>())!.GetProperty("operationId").GetString();
        await fixture.Client.GetAsync($"/todos/operations/{opId}");

        var todos = await fixture.Client.GetFromJsonAsync<dynamic[]>("/todos");
        var id = todos![0].GetProperty("id").GetString();

        var renameResponse = await fixture.Client.PostAsJsonAsync($"/todos/{id}/rename", new { title = "Renamed" });
        renameResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task UpdateProgress_returns_422_for_out_of_range()
    {
        var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "Task" });
        var opId = (await createResponse.Content.ReadFromJsonAsync<dynamic>())!.GetProperty("operationId").GetString();
        await fixture.Client.GetAsync($"/todos/operations/{opId}");
        var todos = await fixture.Client.GetFromJsonAsync<dynamic[]>("/todos");
        var id = todos![0].GetProperty("id").GetString();

        var response = await fixture.Client.PostAsJsonAsync($"/todos/{id}/update-progress", new { progress = 150 });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj \
  --filter "TodoExtendedEndpointTests"
```

Expected: Some fail (rename/progress endpoints missing), others may pass.

- [ ] **Step 3: Update `GET /todos` to read from `TodoSummaries` projection**

In `TodoList.Api/Endpoints/TodoEndpoints.cs`, replace the `GET /todos` handler to read from `db.TodoSummaries` instead of `db.Todos`:

```csharp
app.MapGet("/todos", async (TodoDbContext db, HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    var summaries = await db.TodoSummaries
        .Where(t => t.UserId == userId)
        .OrderBy(t => t.CreatedAt)
        .ToListAsync();

    var now = DateTimeOffset.UtcNow;
    return Results.Ok(summaries.Select(t => new
    {
        t.Id, t.Title, t.IsCompleted, t.CategoryId, t.CategoryName, t.CategoryColor,
        t.DueDate, IsOverdue = t.DueDate.HasValue && !t.IsCompleted && t.DueDate < now,
        t.Progress, t.CreatedAt, t.CompletedAt
    }));
});
```

- [ ] **Step 4: Update `POST /todos` to also write a projection + update `TodoProjectionHandler`**

After creating a todo in `POST /todos`, call `TodoProjectionHandler.HandleAsync(userId, todoCreatedEvent)`.

Inject `TodoProjectionHandler` into the endpoint handler. Add `userId` extraction and pass it to the projection handler.

- [ ] **Step 5: Add new todo sub-endpoints**

Add to `TodoEndpoints.cs` after the existing endpoints:

```csharp
app.MapPost("/todos/{id}/rename", async (
    Guid id, RenameTodoRequest req,
    ITodoRepository repo, TodoProjectionHandler projHandler,
    IOperationRepository opRepo, HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    var todo = await repo.GetByIdAsync(id);
    if (todo is null) return Results.NotFound();

    var result = todo.Rename(req.Title);
    if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

    await repo.SaveAsync();
    foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

    var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
    await opRepo.AddAsync(op); await opRepo.SaveAsync();
    return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id })
        .WithHeader("X-Retry-After-Ms", "200");
});

app.MapPost("/todos/{id}/assign-category", async (
    Guid id, AssignCategoryRequest req,
    ITodoRepository repo, TodoProjectionHandler projHandler,
    IOperationRepository opRepo, HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    var todo = await repo.GetByIdAsync(id);
    if (todo is null) return Results.NotFound();

    var result = todo.AssignCategory(req.CategoryId);
    if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

    await repo.SaveAsync();
    foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

    var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
    await opRepo.AddAsync(op); await opRepo.SaveAsync();
    return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
});

app.MapPost("/todos/{id}/unassign-category", async (
    Guid id,
    ITodoRepository repo, TodoProjectionHandler projHandler,
    IOperationRepository opRepo, HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    var todo = await repo.GetByIdAsync(id);
    if (todo is null) return Results.NotFound();

    var result = todo.UnassignCategory();
    if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

    await repo.SaveAsync();
    foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

    var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
    await opRepo.AddAsync(op); await opRepo.SaveAsync();
    return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
});

app.MapPost("/todos/{id}/set-due-date", async (
    Guid id, SetDueDateRequest req,
    ITodoRepository repo, TodoProjectionHandler projHandler,
    IOperationRepository opRepo, HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    var todo = await repo.GetByIdAsync(id);
    if (todo is null) return Results.NotFound();

    var result = todo.SetDueDate(req.DueDate);
    if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

    await repo.SaveAsync();
    foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

    var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
    await opRepo.AddAsync(op); await opRepo.SaveAsync();
    return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
});

app.MapPost("/todos/{id}/clear-due-date", async (
    Guid id,
    ITodoRepository repo, TodoProjectionHandler projHandler,
    IOperationRepository opRepo, HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    var todo = await repo.GetByIdAsync(id);
    if (todo is null) return Results.NotFound();

    var result = todo.ClearDueDate();
    if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

    await repo.SaveAsync();
    foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

    var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
    await opRepo.AddAsync(op); await opRepo.SaveAsync();
    return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
});

app.MapPost("/todos/{id}/update-notes", async (
    Guid id, UpdateNotesRequest req,
    ITodoRepository repo, TodoProjectionHandler projHandler,
    IOperationRepository opRepo, HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    var todo = await repo.GetByIdAsync(id);
    if (todo is null) return Results.NotFound();

    var result = todo.UpdateNotes(req.Notes);
    if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

    await repo.SaveAsync();
    foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

    var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
    await opRepo.AddAsync(op); await opRepo.SaveAsync();
    return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
});

app.MapPost("/todos/{id}/update-progress", async (
    Guid id, UpdateProgressRequest req,
    ITodoRepository repo, TodoProjectionHandler projHandler,
    IOperationRepository opRepo, HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    var todo = await repo.GetByIdAsync(id);
    if (todo is null) return Results.NotFound();

    var result = todo.UpdateProgress(req.Progress);
    if (!result.IsSuccess) return Results.UnprocessableEntity(new { errors = result.Errors });

    await repo.SaveAsync();
    foreach (var evt in result.Value!) await projHandler.HandleAsync(userId, evt);

    var op = TodoOperation.CreateCompleted(Guid.NewGuid(), id.ToString());
    await opRepo.AddAsync(op); await opRepo.SaveAsync();
    return Results.Accepted($"/todos/operations/{op.Id}", new { operationId = op.Id });
});
```

Add request records at bottom of `TodoEndpoints.cs`:

```csharp
private record RenameTodoRequest(string Title);
private record AssignCategoryRequest(Guid CategoryId);
private record SetDueDateRequest(DateTimeOffset DueDate);
private record UpdateNotesRequest(string? Notes);
private record UpdateProgressRequest(int Progress);
```

- [ ] **Step 6: Run all integration tests**

```bash
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj
```

Expected: All tests pass including new ones.

- [ ] **Step 7: Run all unit tests**

```bash
dotnet test TodoList.Tests/TodoList.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add TodoList.Api/Endpoints/TodoEndpoints.cs TodoList.IntegrationTests/
git commit -m "feat: extend todo endpoints with rename, category, due date, notes, progress"
```

---

### Task 10: Final verification

- [ ] **Step 1: Full build**

```bash
dotnet build TodoList.sln
```

Expected: Build succeeded, 0 errors, 0 warnings (or only nullable warnings).

- [ ] **Step 2: All tests**

```bash
dotnet test TodoList.sln
```

Expected: All unit and integration tests pass.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: Plan A complete — TodoList.Domain + API extension"
```
