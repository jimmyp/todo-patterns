using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using TodoList.Web.Client;
using TodoList.Web.Client.Hubs;
using TodoList.Web.Client.Services;
using TodoList.Web.Client.Store;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices();

// Core stores
builder.Services.AddSingleton<IClientStore, ClientStore>();
builder.Services.AddSingleton<ILocalTodoStore, LocalTodoStore>();
builder.Services.AddSingleton<ILocalCategoryStore, LocalCategoryStore>();

// Services
builder.Services.AddSingleton<IConnectivityService, ConnectivityService>();
builder.Services.AddSingleton<OperationPoller>();
builder.Services.AddSingleton<CommandDispatcher>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton<EventHubClient>();
builder.Services.AddSingleton<StartupSeedService>();

var host = builder.Build();

// Initialize services that need async startup
var clientStore = host.Services.GetRequiredService<IClientStore>() as ClientStore;
if (clientStore is not null)
    await clientStore.InitializeAsync();

var connectivity = host.Services.GetRequiredService<IConnectivityService>() as ConnectivityService;
if (connectivity is not null)
    await connectivity.InitializeAsync();

var seed = host.Services.GetRequiredService<StartupSeedService>();
await seed.SeedAsync();

var hubClient = host.Services.GetRequiredService<EventHubClient>();
await hubClient.StartAsync();

var sync = host.Services.GetRequiredService<SyncService>();
await sync.SyncPendingAsync();

await host.RunAsync();
