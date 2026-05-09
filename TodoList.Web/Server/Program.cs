var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddControllers();

// Reverse proxy for /todos, /categories, /api/me, /hubs/* → Api project. Aspire's
// `WithReference(api)` injects service discovery so "https+http://api" resolves to
// the running API. Keeps the Blazor client talking to a single origin (no CORS).
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "Cookies";
        options.DefaultChallengeScheme = "Google";
    })
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Auth:Google:ClientId"] ?? "dev-google-client-id";
        options.ClientSecret = builder.Configuration["Auth:Google:ClientSecret"] ?? "dev-google-client-secret";
        options.CallbackPath = "/signin-google";
        options.SaveTokens = true;
    })
    .AddGitHub(options =>
    {
        options.ClientId = builder.Configuration["Auth:GitHub:ClientId"] ?? "dev-github-client-id";
        options.ClientSecret = builder.Configuration["Auth:GitHub:ClientSecret"] ?? "dev-github-client-secret";
        options.CallbackPath = "/signin-github";
        options.SaveTokens = true;
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

// Serve Blazor WASM app shell
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapControllers();

// Map proxied routes BEFORE the fallback so /todos/* etc. don't return index.html.
app.MapReverseProxy();

// Fallback: all unmatched routes go to Blazor index.html
app.MapFallbackToFile("index.html");

app.Run();
