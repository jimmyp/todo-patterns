// TodoList.Api/Data/ICategoryListRepository.cs
using TodoList.Domain.Aggregates;

namespace TodoList.Api.Data;

public interface ICategoryListRepository
{
    Task<CategoryList?> GetByUserIdAsync(string userId);
    Task AddAsync(CategoryList categoryList);
    Task SaveAsync();
}
