var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .AddDatabase("todolist");

var api = builder.AddProject<Projects.TodoList_Api>("api")
    .WithReference(sql)
    .WaitFor(sql);

builder.AddProject<Projects.TodoList_Web_Server>("web")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
