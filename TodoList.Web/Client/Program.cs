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

// Stub stores for Plan B — replaced with real implementations in Plan C
builder.Services.AddSingleton<ILocalTodoStore, StubLocalTodoStore>();
builder.Services.AddSingleton<ILocalCategoryStore, StubLocalCategoryStore>();

await builder.Build().RunAsync();
