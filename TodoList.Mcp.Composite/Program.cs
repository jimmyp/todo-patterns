var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => "TodoList MCP Composite — coming in Plan 6");
app.Run();
