var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => "TodoList Web — coming in Plan 2");
app.Run();
