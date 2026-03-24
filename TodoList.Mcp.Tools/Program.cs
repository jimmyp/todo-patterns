var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => "TodoList MCP Tools — coming in Plan 5");
app.Run();
