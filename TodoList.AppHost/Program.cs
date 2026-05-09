// TodoList.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .AddDatabase("todolist");

var api = builder.AddProject<Projects.TodoList_Api>("api")
    .WithReference(sql)
    .WaitFor(sql);

builder.AddProject<Projects.TodoList_Web_Server>("web")
    .WithReference(api)
    .WaitFor(api);

builder.AddProject<Projects.TodoList_Mcp_Tools>("mcp-tools")
    .WithReference(api)
    .WaitFor(api);

builder.AddProject<Projects.TodoList_Mcp_Composite>("mcp-composite")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
