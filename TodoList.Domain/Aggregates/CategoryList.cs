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

    /// <summary>Used by the repository to rehydrate a CategoryList from persistence.</summary>
    public static CategoryList Reconstitute(string userId, int version, IReadOnlyList<Category> categories)
    {
        var list = new CategoryList { UserId = userId, Version = version };
        list._categories.AddRange(categories);
        return list;
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
        Version++;

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
