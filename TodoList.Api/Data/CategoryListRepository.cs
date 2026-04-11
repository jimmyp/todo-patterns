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
