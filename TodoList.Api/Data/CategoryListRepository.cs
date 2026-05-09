// TodoList.Api/Data/CategoryListRepository.cs
using TodoList.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.Data;

/// <summary>
/// Bridges the in-memory <see cref="CategoryList"/> aggregate to the EF-tracked
/// <see cref="CategoryListEntity"/>. The aggregate is rehydrated via
/// <see cref="CategoryList.Reconstitute"/> and is *not* itself tracked. We hold the
/// tracked entity here for the lifetime of the request and diff-apply the aggregate's
/// state onto it in <see cref="SaveAsync"/> so version + categories changes actually
/// land in the DB.
///
/// Scoped to the request: one repo per scope = one tracked entity at most.
/// </summary>
public class CategoryListRepository(TodoDbContext db) : ICategoryListRepository
{
    private CategoryList? _aggregate;
    private CategoryListEntity? _tracked;

    public async Task<CategoryList?> GetByUserIdAsync(string userId)
    {
        _tracked = await db.CategoryLists
            .Include(cl => cl.Categories)
            .FirstOrDefaultAsync(cl => cl.UserId == userId);

        if (_tracked is null) return null;

        _aggregate = CategoryList.Reconstitute(
            _tracked.UserId,
            _tracked.Version,
            _tracked.Categories.Select(c => new Category(c.Id, c.Name, c.Color, c.Icon, c.Order, c.CreatedAt)).ToList());
        return _aggregate;
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
        _tracked = entity;
        _aggregate = categoryList;
    }

    public async Task SaveAsync()
    {
        if (_aggregate is not null && _tracked is not null)
            ApplyAggregateToTracked(_aggregate, _tracked, db);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Reconciles a tracked <see cref="CategoryListEntity"/> with the post-mutation
    /// state of the in-memory aggregate: version + add/update/remove categories.
    /// New categories are explicitly added via <c>db.Categories.Add</c> to ensure they
    /// land in <c>Added</c> state — adding to the parent's nav collection alone gets
    /// them attached as <c>Unchanged</c>, which then becomes a no-op UPDATE that fails
    /// with <c>DbUpdateConcurrencyException</c>.
    /// </summary>
    private static void ApplyAggregateToTracked(CategoryList aggregate, CategoryListEntity tracked, TodoDbContext db)
    {
        tracked.Version = aggregate.Version;

        var aggregateById = aggregate.Categories.ToDictionary(c => c.Id);
        var trackedById = tracked.Categories.ToDictionary(c => c.Id);

        foreach (var trackedCat in tracked.Categories.ToList())
        {
            if (!aggregateById.ContainsKey(trackedCat.Id))
                db.Categories.Remove(trackedCat);
        }

        foreach (var aggCat in aggregate.Categories)
        {
            if (trackedById.TryGetValue(aggCat.Id, out var existing))
            {
                existing.Name = aggCat.Name;
                existing.Color = aggCat.Color;
                existing.Icon = aggCat.Icon;
                existing.Order = aggCat.Order;
            }
            else
            {
                var newEntity = new CategoryEntity
                {
                    Id = aggCat.Id,
                    UserId = aggregate.UserId,
                    Name = aggCat.Name,
                    Color = aggCat.Color,
                    Icon = aggCat.Icon,
                    Order = aggCat.Order,
                    CreatedAt = aggCat.CreatedAt
                };
                db.Categories.Add(newEntity);
                tracked.Categories.Add(newEntity);
            }
        }
    }
}
