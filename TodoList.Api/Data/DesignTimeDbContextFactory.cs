using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TodoList.Api.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TodoDbContext>
{
    public TodoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TodoDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=TodoList;User Id=sa;Password=Password123!;" +
                "TrustServerCertificate=True")
            .Options;
        return new TodoDbContext(options);
    }
}
