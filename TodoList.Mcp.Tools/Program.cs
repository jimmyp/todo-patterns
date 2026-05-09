// TodoList.Mcp.Tools/Program.cs
using TodoList.Mcp.Tools.ApiClient;
using TodoList.Mcp.Tools.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Typed HTTP client pointing at the Api service.
// In Aspire, the URL is injected via service discovery.
// Outside Aspire, configure "ApiBaseUrl" in appsettings.
builder.Services.AddHttpClient<TodoApiClient>(client =>
{
    var baseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl);
    // API key for authentication (set in appsettings or environment)
    var apiKey = builder.Configuration["Auth:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
});

// Register MCP server with all tool types in this assembly
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");

app.Run();
