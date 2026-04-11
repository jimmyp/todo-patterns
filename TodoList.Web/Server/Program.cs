var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddControllers();

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

// Fallback: all unmatched routes go to Blazor index.html
app.MapFallbackToFile("index.html");

app.Run();
