using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TodoList.Api.Auth;
using TodoList.Api.Data;
using TodoList.Api.Endpoints;
using TodoList.Api.EventHandlers;
using TodoList.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerDbContext<TodoDbContext>("todolist", settings =>
{
    // Disable Aspire's auto-registered health check — we register our own below with the "ready" tag
    settings.DisableHealthChecks = true;
});

// Identity DbContext — same SQL Server instance, separate context for identity schema
// AddDbContextFactory registers both the factory (singleton) and scoped DbContext
builder.Services.AddDbContextFactory<AppIdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("todolist")),
    ServiceLifetime.Scoped);

// ASP.NET Core Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
})
.AddEntityFrameworkStores<AppIdentityDbContext>()
.AddDefaultTokenProviders();

// Authentication: Cookie (default) + Google + GitHub + ApiKey
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/api/auth/login";
    options.LogoutPath = "/api/auth/logout";
    options.Events.OnRedirectToLogin = ctx =>
    {
        // Return 401 for API requests instead of redirecting
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
})
.AddGoogle("Google", options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "dev-placeholder";
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "dev-placeholder";
    options.CallbackPath = "/api/auth/callback/google";
})
.AddGitHub("GitHub", options =>
{
    options.ClientId = builder.Configuration["Authentication:GitHub:ClientId"] ?? "dev-placeholder";
    options.ClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"] ?? "dev-placeholder";
    options.CallbackPath = "/api/auth/callback/github";
    options.Scope.Add("user:email");
})
.AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(ApiKeyAuthHandler.SchemeName, _ => { });

// Authorization — require authenticated user by default
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes(
            CookieAuthenticationDefaults.AuthenticationScheme,
            ApiKeyAuthHandler.SchemeName)
        .Build());

builder.Services.AddScoped<ITodoRepository, TodoRepository>();
builder.Services.AddScoped<IOperationRepository, OperationRepository>();
builder.Services.AddScoped<ICategoryListRepository, CategoryListRepository>();
builder.Services.AddScoped<TodoProjectionHandler>();
builder.Services.AddScoped<CategoryProjectionHandler>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TodoDbContext>(tags: ["ready"]);

builder.Services.AddSignalR();

builder.Services.AddOpenApi();

var app = builder.Build();

// Auto-migrate both contexts in development/testing
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var todoDb = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    await todoDb.Database.MigrateAsync();

    var identityDb = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
    await identityDb.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();   // registers /health/live and /health/ready
app.MapTodoEndpoints();
app.MapOperationEndpoints();
app.MapCategoryEndpoints();
app.MapAuthEndpoints();
app.MapHub<EventHub>("/hubs/events");

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.Run();

// Allow WebApplicationFactory to find Program class
public partial class Program { }
