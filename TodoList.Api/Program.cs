using Microsoft.EntityFrameworkCore;
using TodoList.Api.Data;
using TodoList.Api.Endpoints;
using TodoList.Api.EventHandlers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerDbContext<TodoDbContext>("todolist", settings =>
{
    // Disable Aspire's auto-registered health check — we register our own below with the "ready" tag
    settings.DisableHealthChecks = true;
});

builder.Services.AddScoped<ITodoRepository, TodoRepository>();
builder.Services.AddScoped<IOperationRepository, OperationRepository>();
builder.Services.AddScoped<ICategoryListRepository, CategoryListRepository>();
builder.Services.AddScoped<TodoProjectionHandler>();
builder.Services.AddScoped<CategoryProjectionHandler>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TodoDbContext>(tags: ["ready"]);

builder.Services.AddOpenApi();

var app = builder.Build();

// Auto-migrate in development (production uses CI pipeline)
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    await db.Database.MigrateAsync();
}

app.MapDefaultEndpoints();   // registers /health/live and /health/ready
app.MapTodoEndpoints();
app.MapOperationEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.Run();

// Allow WebApplicationFactory to find Program class
public partial class Program { }
