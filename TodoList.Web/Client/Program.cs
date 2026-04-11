using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using TodoList.Web.Client;
using TodoList.Web.Client.Store;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices();

// Real stores (Plan C) — ClientStore registered in Task 10 full DI setup
builder.Services.AddSingleton<IClientStore, ClientStore>();
builder.Services.AddSingleton<ILocalTodoStore, LocalTodoStore>();
builder.Services.AddSingleton<ILocalCategoryStore, LocalCategoryStore>();

await builder.Build().RunAsync();
