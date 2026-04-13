// TodoList.Mcp.Composite/Program.cs
using TodoList.Mcp.Composite.ApiClient;
using TodoList.Mcp.Composite.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Typed HTTP client for the Api
builder.Services.AddHttpClient<TodoApiClient>(client =>
{
    var baseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl);
    var apiKey = builder.Configuration["Auth:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
});

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapPlanEndpoint();
app.MapExecuteEndpoint();

app.Run();

// Marker class for WebApplicationFactory in integration tests
public partial class CompositeProgram { }
