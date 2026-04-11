# Blazor WASM Web UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `TodoList.Web` stub with a Blazor WebAssembly hosted app using MudBlazor — a mobile-first PWA that renders the Todo List, Categories, Task Detail, and Login screens using the Stitch "Clean Dark Foundation" design tokens.

**Architecture:** `TodoList.Web` becomes a Blazor WASM hosted project: a thin ASP.NET Core host (`Server/`) serves the WASM app, handles OAuth login/logout via `AuthController`, and forwards API calls. The WASM client (`Client/`) references `TodoList.Domain` for shared types. UI binds exclusively to `LocalTodoStore` and `LocalCategoryStore` (built in Plan C) — for this plan, those stores are stubbed with hardcoded sample data so screens render correctly. Real store wiring happens in Plan C.

**Tech Stack:** .NET 10, Blazor WebAssembly, MudBlazor (latest stable), ASP.NET Core hosted model, Google Fonts (Space Grotesk, IBM Plex Sans, IBM Plex Mono), Material Symbols

> **Read before starting:** `docs/superpowers/specs/2026-04-07-web-ui-design.md`, `docs/superpowers/specs/2026-04-07-domain-model-extension-design.md`, `TodoList.Web/TodoList.Web.csproj`, `TodoList.Web/Program.cs`, `TodoList.AppHost/Program.cs`

---

## File Map

### Replaced: `TodoList.Web/` → split into Server + Client

```
TodoList.Web/
  Server/
    TodoList.Web.Server.csproj         # ASP.NET Core host — serves WASM, handles OAuth
    Program.cs                         # Auth middleware, static files, fallback routing
    Controllers/
      AuthController.cs                # /auth/google, /auth/github, /auth/logout, /api/me
    appsettings.json
    appsettings.Development.json
    Properties/
      launchSettings.json
  Client/
    TodoList.Web.Client.csproj         # Blazor WASM — references TodoList.Domain
    App.razor
    _Imports.razor
    Program.cs                         # DI registrations, MudBlazor, store stubs
    wwwroot/
      index.html                       # Fonts, Material Symbols, CSS vars
      manifest.webmanifest
      icon-192.png                      # placeholder (1px purple PNG)
      icon-512.png                      # placeholder (1px purple PNG)
      css/
        app.css                        # Design token CSS custom properties
    Layout/
      MainLayout.razor                 # MudLayout + drawer + top bar
      NavMenu.razor                    # Sidebar nav links
      UserProfileStrip.razor           # Bottom of drawer: avatar, name, logout
    Pages/
      TodoList.razor                   # / — Active + Completed task sections
      TodoDetail.razor                 # /todos/{id} — mobile full page
      Categories.razor                 # /categories — category grid
      Login.razor                      # /login — outside shell
    Components/
      TaskRow.razor                    # Single todo row with checkbox, chip, due date
      TaskDialog.razor                 # Add/edit dialog
      CategoryCard.razor               # Category card with color strip
      CategoryDialog.razor             # Create/edit category dialog
      UnsyncedDot.razor                # Subtle dot for unsynced items
      ConflictWarning.razor            # Warning icon for conflicted items
      ConnectivityBanner.razor         # Persistent alert when sync fails
    Store/
      ILocalTodoStore.cs               # Interface — same as Plan C will implement
      ILocalCategoryStore.cs           # Interface — same as Plan C will implement
      StubLocalTodoStore.cs            # Hardcoded sample data for Plan B
      StubLocalCategoryStore.cs        # Hardcoded sample data for Plan B
    Theme/
      AppTheme.cs                      # MudTheme definition with design tokens
```

### Modified: `TodoList.AppHost/Program.cs`
- Update `TodoList.Web` resource reference to use the new Server project path

### Deleted: `TodoList.Web/Program.cs`, `TodoList.Web/TodoList.Web.csproj`
- The top-level stub is replaced by the hosted model

---

## Tasks

### Task 1: Restructure TodoList.Web — Server project

**Files:**
- Create: `TodoList.Web/Server/TodoList.Web.Server.csproj`
- Create: `TodoList.Web/Server/Program.cs`
- Create: `TodoList.Web/Server/appsettings.json`
- Create: `TodoList.Web/Server/appsettings.Development.json`
- Create: `TodoList.Web/Server/Controllers/AuthController.cs`
- Delete: `TodoList.Web/TodoList.Web.csproj`
- Delete: `TodoList.Web/Program.cs`

- [ ] **Step 1: Create the Server project file**

```xml
<!-- TodoList.Web/Server/TodoList.Web.Server.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\TodoList.ServiceDefaults\TodoList.ServiceDefaults.csproj" />
    <ProjectReference Include="..\Client\TodoList.Web.Client.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="10.0.5" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.GitHub" Version="10.0.5" />
  </ItemGroup>

</Project>
```

Note: `Microsoft.AspNetCore.Authentication.GitHub` may not be a first-party package. Use `AspNet.Security.OAuth.GitHub` instead:

```xml
<!-- TodoList.Web/Server/TodoList.Web.Server.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\TodoList.ServiceDefaults\TodoList.ServiceDefaults.csproj" />
    <ProjectReference Include="..\Client\TodoList.Web.Client.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="10.0.5" />
    <PackageReference Include="AspNet.Security.OAuth.GitHub" Version="9.1.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create Server Program.cs**

```csharp
// TodoList.Web/Server/Program.cs
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
```

- [ ] **Step 3: Create AuthController**

```csharp
// TodoList.Web/Server/Controllers/AuthController.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace TodoList.Web.Server.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    [HttpGet("google")]
    public IActionResult GoogleLogin([FromQuery] string returnUrl = "/")
    {
        var props = new AuthenticationProperties { RedirectUri = returnUrl };
        return Challenge(props, "Google");
    }

    [HttpGet("github")]
    public IActionResult GitHubLogin([FromQuery] string returnUrl = "/")
    {
        var props = new AuthenticationProperties { RedirectUri = returnUrl };
        return Challenge(props, "GitHub");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Cookies");
        return Redirect("/login");
    }

    [HttpGet("/api/me")]
    public IActionResult Me()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return Unauthorized();

        return Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            name = User.FindFirstValue(ClaimTypes.Name),
            email = User.FindFirstValue(ClaimTypes.Email),
            avatarUrl = User.FindFirstValue("picture")
        });
    }
}
```

- [ ] **Step 4: Create appsettings files**

```json
// TodoList.Web/Server/appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

```json
// TodoList.Web/Server/appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

- [ ] **Step 5: Delete old stub files**

```bash
rm /Users/jim/code/todo-patterns/TodoList.Web/Program.cs
rm /Users/jim/code/todo-patterns/TodoList.Web/TodoList.Web.csproj
```

- [ ] **Step 6: Update TodoList.sln to replace old Web project with new Server project**

```bash
cd /Users/jim/code/todo-patterns
dotnet sln remove TodoList.Web/TodoList.Web.csproj
dotnet sln add TodoList.Web/Server/TodoList.Web.Server.csproj
```

- [ ] **Step 7: Update AppHost to reference new Server project path**

Read `TodoList.AppHost/Program.cs` first, then find the `TodoList.Web` resource reference and update the path. The AppHost likely has something like:

```csharp
var web = builder.AddProject<Projects.TodoList_Web>("todolist-web");
```

This needs to become a reference to the Server project. In Aspire the project name in `Projects.` is derived from the assembly name. The Server project's assembly will be `TodoList.Web.Server`. Update accordingly:

In `TodoList.AppHost/Program.cs`, change `Projects.TodoList_Web` → `Projects.TodoList_Web_Server`.

Also add a project reference in `TodoList.AppHost/TodoList.AppHost.csproj`:
```xml
<ProjectReference Include="..\TodoList.Web\Server\TodoList.Web.Server.csproj" />
```
And remove the old reference:
```xml
<!-- Remove: -->
<ProjectReference Include="..\TodoList.Web\TodoList.Web.csproj" />
```

- [ ] **Step 8: Verify Server project builds**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Web/Server/TodoList.Web.Server.csproj
```

Expected: Build succeeds (the Client project doesn't exist yet so expect a reference error — that's OK, fix in Task 2).

- [ ] **Step 9: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Server/ TodoList.AppHost/ TodoList.sln
git commit -m "feat: scaffold TodoList.Web.Server host project for Blazor WASM hosted model"
```

---

### Task 2: Create the Blazor WASM Client project skeleton

**Files:**
- Create: `TodoList.Web/Client/TodoList.Web.Client.csproj`
- Create: `TodoList.Web/Client/Program.cs`
- Create: `TodoList.Web/Client/_Imports.razor`
- Create: `TodoList.Web/Client/App.razor`
- Create: `TodoList.Web/Client/wwwroot/index.html`
- Create: `TodoList.Web/Client/wwwroot/manifest.webmanifest`
- Create: `TodoList.Web/Client/wwwroot/css/app.css`

- [ ] **Step 1: Create Client project file**

```xml
<!-- TodoList.Web/Client/TodoList.Web.Client.csproj -->
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\TodoList.Domain\TodoList.Domain.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.5" />
    <PackageReference Include="MudBlazor" Version="8.*" />
  </ItemGroup>

  <ItemGroup>
    <ServiceWorker Include="wwwroot\service-worker.js" PublishedContent="wwwroot\service-worker.published.js" />
  </ItemGroup>

</Project>
```

Note: If `TodoList.Domain` doesn't exist yet (Plan A not run), comment out the `ProjectReference` temporarily.

- [ ] **Step 2: Create Client Program.cs**

```csharp
// TodoList.Web/Client/Program.cs
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using TodoList.Web.Client.Store;
using TodoList.Web.Client.Theme;

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
```

- [ ] **Step 3: Create App.razor**

```razor
@* TodoList.Web/Client/App.razor *@
<MudThemeProvider Theme="AppTheme.Theme" IsDarkMode="true" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<Router AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
            <NotAuthorized>
                <RedirectToLogin />
            </NotAuthorized>
        </AuthorizeRouteView>
    </Found>
    <NotFound>
        <PageTitle>Not Found</PageTitle>
        <LayoutView Layout="@typeof(MainLayout)">
            <MudText Typo="Typo.h6">Page not found.</MudText>
        </LayoutView>
    </NotFound>
</Router>
```

Note: `AuthorizeRouteView` and `RedirectToLogin` require auth state. For Plan B, simplify to avoid auth complexity — just use `RouteView`:

```razor
@* TodoList.Web/Client/App.razor *@
<MudThemeProvider Theme="AppTheme.Theme" IsDarkMode="true" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<Router AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
    </Found>
    <NotFound>
        <PageTitle>Not Found</PageTitle>
        <LayoutView Layout="@typeof(MainLayout)">
            <MudText Typo="Typo.h6">Page not found.</MudText>
        </LayoutView>
    </NotFound>
</Router>
```

- [ ] **Step 4: Create _Imports.razor**

```razor
@* TodoList.Web/Client/_Imports.razor *@
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.JSInterop
@using MudBlazor
@using TodoList.Web.Client
@using TodoList.Web.Client.Layout
@using TodoList.Web.Client.Pages
@using TodoList.Web.Client.Components
@using TodoList.Web.Client.Store
@using TodoList.Web.Client.Theme
```

- [ ] **Step 5: Create index.html**

```html
<!-- TodoList.Web/Client/wwwroot/index.html -->
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>TodoList</title>
    <base href="/" />

    <!-- Design system fonts -->
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600;700&family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap" rel="stylesheet">

    <!-- Material Symbols -->
    <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@20..48,100..700,0..1,-50..200" />

    <!-- MudBlazor CSS -->
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />

    <!-- App design tokens -->
    <link href="css/app.css" rel="stylesheet" />

    <!-- PWA manifest -->
    <link rel="manifest" href="manifest.webmanifest" />
    <meta name="theme-color" content="#8B5CF6" />
</head>
<body>
    <div id="app">
        <div style="display:flex;justify-content:center;align-items:center;height:100vh;background:#0B0D10;">
            <svg width="48" height="48" viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <circle cx="24" cy="24" r="20" stroke="#8B5CF6" stroke-width="3" stroke-dasharray="31.4 31.4">
                    <animateTransform attributeName="transform" type="rotate" from="0 24 24" to="360 24 24" dur="1s" repeatCount="indefinite"/>
                </circle>
            </svg>
        </div>
    </div>
    <div id="blazor-error-ui" style="display:none;background:#EF4444;color:white;padding:1rem;position:fixed;bottom:0;width:100%;z-index:9999;">
        An unhandled error has occurred. <a href="" style="color:white;font-weight:bold;">Reload</a>
    </div>
    <script src="_framework/blazor.webassembly.js"></script>
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
</body>
</html>
```

- [ ] **Step 6: Create CSS design tokens**

```css
/* TodoList.Web/Client/wwwroot/css/app.css */
:root {
    --bg-app: #0B0D10;
    --bg-surface: #161920;
    --bg-surface-hover: #1F242D;
    --text-main: #E2E8F0;
    --text-muted: #8492A6;
    --border: #1F242D;
    --primary: #8B5CF6;
    --success: #00FF9D;
    --error: #EF4444;
    --warning: #F59E0B;
    --font-heading: 'Space Grotesk', sans-serif;
    --font-body: 'IBM Plex Sans', sans-serif;
    --font-mono: 'IBM Plex Mono', monospace;
    --radius: 4px;
    --transition: 150ms cubic-bezier(0.4, 0, 0.2, 1);
}

html, body {
    background-color: var(--bg-app);
    color: var(--text-main);
    font-family: var(--font-body);
}

.mud-typography-h1,
.mud-typography-h2,
.mud-typography-h3,
.mud-typography-h4,
.mud-typography-h5,
.mud-typography-h6 {
    font-family: var(--font-heading);
}

.font-mono {
    font-family: var(--font-mono);
    font-size: 0.75rem;
    text-transform: uppercase;
    letter-spacing: 0.05em;
}

.unsynced-dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background-color: var(--text-muted);
    display: inline-block;
}

.task-row--completed {
    opacity: 0.6;
}

.task-row--completed .mud-typography {
    text-decoration: line-through;
}

.category-card {
    position: relative;
    border-left: 4px solid transparent;
    transition: border-color var(--transition);
}

.category-card:hover {
    border-color: var(--primary);
}
```

- [ ] **Step 7: Create PWA manifest**

```json
{
  "name": "TodoList",
  "short_name": "Todos",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#0B0D10",
  "theme_color": "#8B5CF6",
  "icons": [
    { "src": "icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "icon-512.png", "sizes": "512x512", "type": "image/png" }
  ]
}
```

- [ ] **Step 8: Create placeholder PWA icons**

These are minimal valid 1×1 purple PNGs — replace with real icons before going live.

```bash
# Create 1x1 purple PNG for icon-192.png and icon-512.png
# Use a simple Python script or just create placeholder files:
python3 -c "
import struct, zlib

def make_png(width, height, r, g, b):
    def make_chunk(chunk_type, data):
        c = chunk_type + data
        return struct.pack('>I', len(data)) + c + struct.pack('>I', zlib.crc32(c) & 0xFFFFFFFF)

    signature = b'\\x89PNG\\r\\n\\x1a\\n'
    ihdr = make_chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
    raw = b'\\x00' + bytes([r, g, b]) * width
    idat = make_chunk(b'IDAT', zlib.compress(raw * height))
    iend = make_chunk(b'IEND', b'')
    return signature + ihdr + idat + iend

data = make_png(1, 1, 139, 92, 246)
with open('TodoList.Web/Client/wwwroot/icon-192.png', 'wb') as f: f.write(data)
with open('TodoList.Web/Client/wwwroot/icon-512.png', 'wb') as f: f.write(data)
print('Icons created')
"
```

- [ ] **Step 9: Add the sln reference for the Client project**

```bash
cd /Users/jim/code/todo-patterns
dotnet sln add TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 10: Build both projects**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
dotnet build TodoList.Web/Server/TodoList.Web.Server.csproj
```

Expected: Both build. If `TodoList.Domain` is missing (Plan A not complete), comment out that `ProjectReference` temporarily.

- [ ] **Step 11: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/ TodoList.sln
git commit -m "feat: create Blazor WASM client project skeleton with MudBlazor and design tokens"
```

---

### Task 3: MudBlazor theme + store interfaces + stubs

**Files:**
- Create: `TodoList.Web/Client/Theme/AppTheme.cs`
- Create: `TodoList.Web/Client/Store/ILocalTodoStore.cs`
- Create: `TodoList.Web/Client/Store/ILocalCategoryStore.cs`
- Create: `TodoList.Web/Client/Store/StubLocalTodoStore.cs`
- Create: `TodoList.Web/Client/Store/StubLocalCategoryStore.cs`

- [ ] **Step 1: Create AppTheme**

```csharp
// TodoList.Web/Client/Theme/AppTheme.cs
using MudBlazor;

namespace TodoList.Web.Client.Theme;

public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#8B5CF6",
            Success = "#00FF9D",
            Error = "#EF4444",
            Warning = "#F59E0B",
            Background = "#0B0D10",
            Surface = "#161920",
            AppbarBackground = "#161920",
            DrawerBackground = "#161920",
            DrawerText = "#E2E8F0",
            TextPrimary = "#E2E8F0",
            TextSecondary = "#8492A6",
            ActionDefault = "#8492A6",
            Divider = "#1F242D",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#8B5CF6",
            Success = "#00FF9D",
            Error = "#EF4444",
            Warning = "#F59E0B",
            Background = "#0B0D10",
            Surface = "#161920",
            AppbarBackground = "#161920",
            DrawerBackground = "#161920",
            DrawerText = "#E2E8F0",
            TextPrimary = "#E2E8F0",
            TextSecondary = "#8492A6",
            ActionDefault = "#8492A6",
            Divider = "#1F242D",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["IBM Plex Sans", "sans-serif"]
            },
            H1 = new H1Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H2 = new H2Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H3 = new H3Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H4 = new H4Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H5 = new H5Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H6 = new H6Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
        },
        Shape = new Shape
        {
            BorderRadius = 4
        }
    };
}
```

- [ ] **Step 2: Create store interfaces**

The interfaces mirror what Plan C will implement with real `ClientStore` + `localStorage`. These types reference `TodoList.Domain` read models.

```csharp
// TodoList.Web/Client/Store/ILocalTodoStore.cs
using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public interface ILocalTodoStore
{
    IReadOnlyList<TodoSummary> Todos { get; }
    TodoSummary? GetById(string id);
    event Action OnChange;
}
```

```csharp
// TodoList.Web/Client/Store/ILocalCategoryStore.cs
using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public interface ILocalCategoryStore
{
    IReadOnlyList<CategorySummary> Categories { get; }
    CategorySummary? GetById(string id);;
    event Action OnChange;
}
```

- [ ] **Step 3: Create stub stores with sample data**

```csharp
// TodoList.Web/Client/Store/StubLocalTodoStore.cs
using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public class StubLocalTodoStore : ILocalTodoStore
{
    private static readonly List<TodoSummary> _todos =
    [
        new TodoSummary
        {
            Id = "1",
            Title = "Review architecture spec",
            IsCompleted = false,
            CategoryId = "cat-1",
            CategoryName = "Work",
            CategoryColor = "#F59E0B",
            DueDate = DateTimeOffset.UtcNow.AddDays(1),
            IsOverdue = false,
            Progress = 60,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            CompletedAt = null
        },
        new TodoSummary
        {
            Id = "2",
            Title = "Set up dev container",
            IsCompleted = false,
            CategoryId = "cat-1",
            CategoryName = "Work",
            CategoryColor = "#F59E0B",
            DueDate = DateTimeOffset.UtcNow.AddDays(-1),
            IsOverdue = true,
            Progress = 0,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            CompletedAt = null
        },
        new TodoSummary
        {
            Id = "3",
            Title = "Buy groceries",
            IsCompleted = false,
            CategoryId = "cat-2",
            CategoryName = "Personal",
            CategoryColor = "#8B5CF6",
            DueDate = null,
            IsOverdue = false,
            Progress = 0,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CompletedAt = null
        },
        new TodoSummary
        {
            Id = "4",
            Title = "Update resume",
            IsCompleted = true,
            CategoryId = null,
            CategoryName = null,
            CategoryColor = null,
            DueDate = null,
            IsOverdue = false,
            Progress = 100,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
            CompletedAt = DateTimeOffset.UtcNow.AddDays(-2)
        },
    ];

    public IReadOnlyList<TodoSummary> Todos => _todos.AsReadOnly();
    public TodoSummary? GetById(string id) => _todos.FirstOrDefault(t => t.Id == id);
    public event Action OnChange = delegate { };
}
```

```csharp
// TodoList.Web/Client/Store/StubLocalCategoryStore.cs
using TodoList.Domain.ReadModels;

namespace TodoList.Web.Client.Store;

public class StubLocalCategoryStore : ILocalCategoryStore
{
    private static readonly List<CategorySummary> _categories =
    [
        new CategorySummary { Id = "cat-1", Name = "Work",     Color = "#F59E0B", Icon = "work",          Order = 1, TodoCount = 2 },
        new CategorySummary { Id = "cat-2", Name = "Personal", Color = "#8B5CF6", Icon = "person",         Order = 2, TodoCount = 1 },
        new CategorySummary { Id = "cat-3", Name = "Urgent",   Color = "#EF4444", Icon = "priority_high",  Order = 3, TodoCount = 0 },
        new CategorySummary { Id = "cat-4", Name = "Design",   Color = "#0EA5E9", Icon = "palette",        Order = 4, TodoCount = 0 },
    ];

    public IReadOnlyList<CategorySummary> Categories => _categories.AsReadOnly();
    public CategorySummary? GetById(string id) => _categories.FirstOrDefault(c => c.Id == id);
    public event Action OnChange = delegate { };
}
```

- [ ] **Step 4: Build**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

Expected: Build succeeds. If `TodoList.Domain.ReadModels.TodoSummary` / `CategorySummary` don't exist yet, create minimal stubs inline:

```csharp
// TodoList.Web/Client/Store/ReadModelStubs.cs
// Temporary — remove after Plan A creates TodoList.Domain
namespace TodoList.Domain.ReadModels;

public class TodoSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryColor { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public int Progress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class CategorySummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#8B5CF6";
    public string Icon { get; set; } = "label";
    public int Order { get; set; }
    public int TodoCount { get; set; }
}
```

- [ ] **Step 5: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Theme/ TodoList.Web/Client/Store/
git commit -m "feat: add MudBlazor theme, store interfaces, and stub stores"
```

---

### Task 4: Shell layout — MainLayout, NavMenu, UserProfileStrip

**Files:**
- Create: `TodoList.Web/Client/Layout/MainLayout.razor`
- Create: `TodoList.Web/Client/Layout/NavMenu.razor`
- Create: `TodoList.Web/Client/Layout/UserProfileStrip.razor`

- [ ] **Step 1: Create MainLayout.razor**

Desktop: persistent 240px drawer with nav + user strip. Mobile: AppBar + temporary drawer + bottom navigation.

```razor
@* TodoList.Web/Client/Layout/MainLayout.razor *@
@inherits LayoutComponentBase
@inject NavigationManager Nav

<MudLayout>
    @* Desktop: persistent left drawer *@
    <MudDrawer @bind-Open="_drawerOpen"
               Variant="@DrawerVariant"
               ClipMode="DrawerClipMode.Always"
               Elevation="1"
               Width="240px"
               Style="background:var(--bg-surface);">
        <MudDrawerHeader Style="padding: 1.5rem 1rem 1rem;">
            <MudText Typo="Typo.h6" Style="font-family:var(--font-heading);color:var(--primary);font-weight:700;">
                TodoList
            </MudText>
        </MudDrawerHeader>
        <NavMenu />
        <MudSpacer />
        <UserProfileStrip />
    </MudDrawer>

    @* Mobile: top app bar *@
    <MudAppBar Elevation="1"
               Style="background:var(--bg-surface);display:none;"
               Class="d-flex d-md-none">
        <MudIconButton Icon="@Icons.Material.Filled.Menu"
                       Color="Color.Inherit"
                       Edge="Edge.Start"
                       OnClick="ToggleDrawer" />
        <MudText Typo="Typo.h6" Style="font-family:var(--font-heading);color:var(--primary);font-weight:700;">
            TodoList
        </MudText>
    </MudAppBar>

    <MudMainContent Style="background:var(--bg-app);min-height:100vh;padding-top:0;">
        <MudContainer MaxWidth="MaxWidth.ExtraLarge" Style="padding: 1.5rem;">
            @Body
        </MudContainer>
    </MudMainContent>

    @* Mobile: bottom navigation *@
    <MudBottomNavigation SelectedIndex="_bottomNavIndex"
                         SelectedIndexChanged="OnBottomNavChanged"
                         Style="background:var(--bg-surface);display:none;"
                         Class="d-flex d-md-none">
        <MudBottomNavigationItem Label="Todos" Icon="@Icons.Material.Filled.CheckCircle" />
        <MudBottomNavigationItem Label="Categories" Icon="@Icons.Material.Filled.Category" />
    </MudBottomNavigation>
</MudLayout>

@code {
    private bool _drawerOpen = true;
    private int _bottomNavIndex = 0;

    private DrawerVariant DrawerVariant =>
        _isMobile ? DrawerVariant.Temporary : DrawerVariant.Persistent;

    private bool _isMobile = false;

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;

    private void OnBottomNavChanged(int index)
    {
        _bottomNavIndex = index;
        Nav.NavigateTo(index == 0 ? "/" : "/categories");
    }
}
```

- [ ] **Step 2: Create NavMenu.razor**

```razor
@* TodoList.Web/Client/Layout/NavMenu.razor *@
@inject NavigationManager Nav

<MudNavMenu>
    <MudNavLink Href="/"
                Match="NavLinkMatch.All"
                Icon="@Icons.Material.Filled.CheckCircle"
                IconColor="Color.Primary">
        Todo List
    </MudNavLink>
    <MudNavLink Href="/categories"
                Match="NavLinkMatch.Prefix"
                Icon="@Icons.Material.Filled.Category"
                IconColor="Color.Primary">
        Categories
    </MudNavLink>
</MudNavMenu>
```

- [ ] **Step 3: Create UserProfileStrip.razor**

```razor
@* TodoList.Web/Client/Layout/UserProfileStrip.razor *@

<div style="padding: 1rem; border-top: 1px solid var(--border); display: flex; align-items: center; gap: 0.75rem;">
    <MudAvatar Style="width:32px;height:32px;background:var(--primary);">
        <MudIcon Icon="@Icons.Material.Filled.Person" Size="Size.Small" />
    </MudAvatar>
    <div style="flex:1;min-width:0;">
        <MudText Typo="Typo.body2" Style="color:var(--text-main);font-weight:500;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">
            Jim
        </MudText>
        <MudText Typo="Typo.caption" Style="color:var(--text-muted);">
            jim@example.com
        </MudText>
    </div>
    <MudIconButton Icon="@Icons.Material.Filled.Logout"
                   Size="Size.Small"
                   Style="color:var(--text-muted);"
                   OnClick="Logout"
                   Title="Sign out" />
</div>

@code {
    private void Logout()
    {
        // Plan C will POST to /auth/logout via HttpClient
        // For now, navigate to login page
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 5: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Layout/
git commit -m "feat: add shell layout — MainLayout, NavMenu, UserProfileStrip"
```

---

### Task 5: Shared components — UnsyncedDot, ConflictWarning, ConnectivityBanner

**Files:**
- Create: `TodoList.Web/Client/Components/UnsyncedDot.razor`
- Create: `TodoList.Web/Client/Components/ConflictWarning.razor`
- Create: `TodoList.Web/Client/Components/ConnectivityBanner.razor`

- [ ] **Step 1: Create UnsyncedDot.razor**

```razor
@* TodoList.Web/Client/Components/UnsyncedDot.razor *@

@if (Show)
{
    <span class="unsynced-dot" title="Not yet synced to server"></span>
}

@code {
    [Parameter] public bool Show { get; set; }
}
```

- [ ] **Step 2: Create ConflictWarning.razor**

```razor
@* TodoList.Web/Client/Components/ConflictWarning.razor *@

@if (Show)
{
    <MudTooltip Text="@(Message ?? "Couldn't be saved — review and correct")">
        <MudIcon Icon="@Icons.Material.Filled.Warning"
                 Size="Size.Small"
                 Style="color:var(--warning);cursor:pointer;"
                 @onclick="OnClick" />
    </MudTooltip>
}

@code {
    [Parameter] public bool Show { get; set; }
    [Parameter] public string? Message { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
}
```

- [ ] **Step 3: Create ConnectivityBanner.razor**

```razor
@* TodoList.Web/Client/Components/ConnectivityBanner.razor *@

@if (Show)
{
    <MudAlert Severity="Severity.Error"
              Variant="Variant.Filled"
              Style="border-radius:var(--radius);margin-bottom:1rem;"
              ShowCloseIcon="false">
        <div style="display:flex;align-items:center;justify-content:space-between;width:100%;">
            <span>Some changes couldn't sync.</span>
            <MudButton Variant="Variant.Text"
                       Color="Color.Inherit"
                       Size="Size.Small"
                       OnClick="OnRetry">
                Retry
            </MudButton>
        </div>
    </MudAlert>
}

@code {
    [Parameter] public bool Show { get; set; }
    [Parameter] public EventCallback OnRetry { get; set; }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 5: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Components/UnsyncedDot.razor TodoList.Web/Client/Components/ConflictWarning.razor TodoList.Web/Client/Components/ConnectivityBanner.razor
git commit -m "feat: add shared status components — UnsyncedDot, ConflictWarning, ConnectivityBanner"
```

---

### Task 6: TaskRow and TaskDialog components

**Files:**
- Create: `TodoList.Web/Client/Components/TaskRow.razor`
- Create: `TodoList.Web/Client/Components/TaskDialog.razor`

- [ ] **Step 1: Create TaskRow.razor**

```razor
@* TodoList.Web/Client/Components/TaskRow.razor *@
@using TodoList.Domain.ReadModels

<MudListItem Style="@($"background:{(IsHovered ? "var(--bg-surface-hover)" : "transparent")};border-radius:var(--radius);padding:0.5rem 0.75rem;transition:background var(--transition);")"
             @onmouseenter="() => IsHovered = true"
             @onmouseleave="() => IsHovered = false">
    <div style="display:flex;align-items:center;gap:0.75rem;">

        @* Checkbox *@
        <MudCheckBox T="bool"
                     Value="@Todo.IsCompleted"
                     ValueChanged="OnCompletedChanged"
                     Color="Color.Success"
                     Style="@(Todo.IsCompleted ? "color:var(--success);" : "")" />

        @* Title + metadata *@
        <div style="flex:1;min-width:0;" class="@(Todo.IsCompleted ? "task-row--completed" : "")">
            <MudText Typo="Typo.body1" Style="color:var(--text-main);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">
                @Todo.Title
            </MudText>
            <div style="display:flex;align-items:center;gap:0.5rem;margin-top:0.25rem;flex-wrap:wrap;">
                @if (Todo.CategoryName != null)
                {
                    <MudChip T="string"
                             Size="Size.Small"
                             Style="@($"background:{Todo.CategoryColor}22;color:{Todo.CategoryColor};font-family:var(--font-mono);font-size:0.65rem;text-transform:uppercase;letter-spacing:0.05em;height:18px;")"
                             DisableRipple="true">
                        @Todo.CategoryName
                    </MudChip>
                }
                @if (Todo.DueDate.HasValue)
                {
                    <span class="font-mono" style="@($"color:{(Todo.IsOverdue ? "var(--error)" : "var(--text-muted)")};font-size:0.7rem;display:flex;align-items:center;gap:2px;")">
                        @if (Todo.IsOverdue)
                        {
                            <MudIcon Icon="@Icons.Material.Filled.PriorityHigh" Size="Size.Small" Style="font-size:0.8rem;" />
                        }
                        @Todo.DueDate.Value.ToString("MMM d")
                    </span>
                }
            </div>
            @if (Todo.Progress > 0)
            {
                <MudProgressLinear Value="@Todo.Progress"
                                   Color="@(Todo.IsCompleted ? Color.Success : Color.Primary)"
                                   Size="Size.Small"
                                   Style="margin-top:0.375rem;border-radius:2px;" />
            }
        </div>

        @* Status indicators *@
        <div style="display:flex;align-items:center;gap:0.375rem;">
            <UnsyncedDot Show="@IsUnsynced" />
            <ConflictWarning Show="@IsConflicted" OnClick="NavigateToDetail" />
        </div>

        @* Overflow menu — visible on hover *@
        @if (IsHovered)
        {
            <MudMenu Icon="@Icons.Material.Filled.MoreVert"
                     Size="Size.Small"
                     Style="color:var(--text-muted);">
                <MudMenuItem OnClick="NavigateToDetail">Edit</MudMenuItem>
                <MudMenuItem OnClick="OnDelete" Style="color:var(--error);">Delete</MudMenuItem>
            </MudMenu>
        }
    </div>
</MudListItem>

@code {
    [Parameter, EditorRequired] public TodoSummary Todo { get; set; } = null!;
    [Parameter] public bool IsUnsynced { get; set; }
    [Parameter] public bool IsConflicted { get; set; }
    [Parameter] public EventCallback<bool> OnCompletedChanged { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback OnEdit { get; set; }

    private bool IsHovered { get; set; }

    [Inject] private NavigationManager Nav { get; set; } = null!;

    private void NavigateToDetail() => Nav.NavigateTo($"/todos/{Todo.Id}");
}
```

- [ ] **Step 2: Create TaskDialog.razor**

```razor
@* TodoList.Web/Client/Components/TaskDialog.razor *@
@using TodoList.Domain.ReadModels

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6" Style="font-family:var(--font-heading);">
            @(IsEdit ? "Edit Task" : "Add Task")
        </MudText>
    </TitleContent>
    <DialogContent>
        <MudStack Spacing="3">
            <MudTextField @bind-Value="Title"
                          Label="Title"
                          Variant="Variant.Outlined"
                          Required="true"
                          RequiredError="Title is required"
                          Immediate="true"
                          Style="--mud-palette-text-primary:var(--text-main);" />

            <MudSelect T="string"
                       @bind-Value="SelectedCategoryId"
                       Label="Category (optional)"
                       Variant="Variant.Outlined"
                       Clearable="true">
                @foreach (var cat in Categories)
                {
                    <MudSelectItem T="string" Value="@cat.Id">
                        <div style="display:flex;align-items:center;gap:0.5rem;">
                            <span style="@($"width:10px;height:10px;border-radius:2px;background:{cat.Color};display:inline-block;")"></span>
                            @cat.Name
                        </div>
                    </MudSelectItem>
                }
            </MudSelect>

            <MudDatePicker @bind-Date="DueDate"
                           Label="Due date (optional)"
                           Variant="Variant.Outlined"
                           Clearable="true" />

            <MudTextField @bind-Value="Notes"
                          Label="Notes (optional)"
                          Variant="Variant.Outlined"
                          Lines="3"
                          Immediate="true" />

            <MudSlider @bind-Value="Progress"
                       Min="0"
                       Max="100"
                       Step="5"
                       Color="Color.Primary">
                Progress: @Progress%
            </MudSlider>
        </MudStack>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel" Variant="Variant.Text" Style="color:var(--text-muted);">Cancel</MudButton>
        <MudButton OnClick="Submit"
                   Variant="Variant.Filled"
                   Color="Color.Primary"
                   Disabled="@string.IsNullOrWhiteSpace(Title)">
            @(IsEdit ? "Save" : "Add")
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter] public bool IsEdit { get; set; }
    [Parameter] public string? InitialTitle { get; set; }
    [Parameter] public string? InitialCategoryId { get; set; }
    [Parameter] public DateTime? InitialDueDate { get; set; }
    [Parameter] public string? InitialNotes { get; set; }
    [Parameter] public int InitialProgress { get; set; }
    [Parameter] public IReadOnlyList<CategorySummary> Categories { get; set; } = [];

    private string Title { get; set; } = "";
    private string? SelectedCategoryId { get; set; }
    private DateTime? DueDate { get; set; }
    private string? Notes { get; set; }
    private int Progress { get; set; }

    protected override void OnParametersSet()
    {
        Title = InitialTitle ?? "";
        SelectedCategoryId = InitialCategoryId;
        DueDate = InitialDueDate;
        Notes = InitialNotes;
        Progress = InitialProgress;
    }

    private void Cancel() => MudDialog.Cancel();

    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(Title)) return;
        MudDialog.Close(DialogResult.Ok(new TaskDialogResult
        {
            Title = Title,
            CategoryId = SelectedCategoryId,
            DueDate = DueDate.HasValue ? new DateTimeOffset(DueDate.Value, TimeSpan.Zero) : null,
            Notes = Notes,
            Progress = Progress
        }));
    }
}
```

Add a result record to the same file:

```csharp
// At bottom of TaskDialog.razor @code block — or as a separate file:
// TodoList.Web/Client/Components/TaskDialogResult.cs
namespace TodoList.Web.Client.Components;

public record TaskDialogResult
{
    public string Title { get; init; } = "";
    public string? CategoryId { get; init; }
    public DateTimeOffset? DueDate { get; init; }
    public string? Notes { get; init; }
    public int Progress { get; init; }
}
```

Create `TodoList.Web/Client/Components/TaskDialogResult.cs`:

```csharp
// TodoList.Web/Client/Components/TaskDialogResult.cs
namespace TodoList.Web.Client.Components;

public record TaskDialogResult
{
    public string Title { get; init; } = "";
    public string? CategoryId { get; init; }
    public DateTimeOffset? DueDate { get; init; }
    public string? Notes { get; init; }
    public int Progress { get; init; }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 4: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Components/TaskRow.razor TodoList.Web/Client/Components/TaskDialog.razor TodoList.Web/Client/Components/TaskDialogResult.cs
git commit -m "feat: add TaskRow and TaskDialog components"
```

---

### Task 7: CategoryCard and CategoryDialog components

**Files:**
- Create: `TodoList.Web/Client/Components/CategoryCard.razor`
- Create: `TodoList.Web/Client/Components/CategoryDialog.razor`
- Create: `TodoList.Web/Client/Components/CategoryDialogResult.cs`

- [ ] **Step 1: Create CategoryCard.razor**

```razor
@* TodoList.Web/Client/Components/CategoryCard.razor *@
@using TodoList.Domain.ReadModels

<MudCard Style="@($"background:var(--bg-surface);border-radius:var(--radius);border-left:4px solid {Category.Color};cursor:pointer;transition:border-color var(--transition),background var(--transition);")"
         @onmouseenter="() => IsHovered = true"
         @onmouseleave="() => IsHovered = false"
         @onclick="OnCardClick"
         Class="@(IsHovered ? "category-card-hover" : "")">
    <MudCardContent Style="padding:1rem;">
        <div style="display:flex;align-items:flex-start;justify-content:space-between;">
            <div style="display:flex;align-items:center;gap:0.75rem;">
                <span class="material-symbols-outlined" style="@($"color:{Category.Color};font-size:1.5rem;")">
                    @Category.Icon
                </span>
                <div>
                    <MudText Typo="Typo.body1" Style="color:var(--text-main);font-weight:500;">
                        @Category.Name
                    </MudText>
                    <span class="font-mono" style="background:var(--bg-surface-hover);color:var(--text-muted);padding:1px 6px;border-radius:2px;font-size:0.65rem;">
                        @Category.TodoCount TASKS
                    </span>
                </div>
            </div>
            @if (IsHovered)
            {
                <MudMenu Icon="@Icons.Material.Filled.MoreVert" Size="Size.Small" Style="color:var(--text-muted);">
                    <MudMenuItem OnClick="OnEdit">Edit</MudMenuItem>
                    <MudMenuItem OnClick="OnDelete" Style="color:var(--error);">Delete</MudMenuItem>
                </MudMenu>
            }
        </div>
    </MudCardContent>
</MudCard>

@code {
    [Parameter, EditorRequired] public CategorySummary Category { get; set; } = null!;
    [Parameter] public EventCallback OnEdit { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback OnCardClick { get; set; }

    private bool IsHovered { get; set; }
}
```

- [ ] **Step 2: Create CategoryDialog.razor**

```razor
@* TodoList.Web/Client/Components/CategoryDialog.razor *@

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6" Style="font-family:var(--font-heading);">
            @(IsEdit ? "Edit Category" : "New Category")
        </MudText>
    </TitleContent>
    <DialogContent>
        <MudStack Spacing="3">
            <MudTextField @bind-Value="Name"
                          Label="Name"
                          Variant="Variant.Outlined"
                          Required="true"
                          RequiredError="Name is required"
                          MaxLength="50"
                          Immediate="true" />

            <div>
                <MudText Typo="Typo.body2" Style="color:var(--text-muted);margin-bottom:0.5rem;">Color</MudText>
                <div style="display:flex;gap:0.5rem;align-items:center;flex-wrap:wrap;">
                    @foreach (var preset in _presetColors)
                    {
                        <button @onclick="() => Color = preset"
                                style="@($"width:28px;height:28px;border-radius:4px;background:{preset};border:{(Color == preset ? "3px solid white" : "2px solid transparent")};cursor:pointer;padding:0;")"
                                title="@preset" />
                    }
                    <MudTextField @bind-Value="Color"
                                  Variant="Variant.Outlined"
                                  Style="width:120px;font-family:var(--font-mono);font-size:0.8rem;"
                                  Placeholder="#8B5CF6" />
                </div>
            </div>

            <MudTextField @bind-Value="Icon"
                          Label="Icon (Material Symbol name)"
                          Variant="Variant.Outlined"
                          Placeholder="e.g. work, person, palette"
                          HelperText="Material Symbol icon name"
                          Immediate="true" />

            @if (!string.IsNullOrWhiteSpace(Icon))
            {
                <div style="display:flex;align-items:center;gap:0.5rem;">
                    <MudText Typo="Typo.caption" Style="color:var(--text-muted);">Preview:</MudText>
                    <span class="material-symbols-outlined" style="@($"color:{Color};")">@Icon</span>
                </div>
            }
        </MudStack>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel" Variant="Variant.Text" Style="color:var(--text-muted);">Cancel</MudButton>
        <MudButton OnClick="Submit"
                   Variant="Variant.Filled"
                   Color="Color.Primary"
                   Disabled="@(string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Color))">
            @(IsEdit ? "Save" : "Create")
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter] public bool IsEdit { get; set; }
    [Parameter] public string? InitialName { get; set; }
    [Parameter] public string InitialColor { get; set; } = "#8B5CF6";
    [Parameter] public string? InitialIcon { get; set; }

    private string Name { get; set; } = "";
    private string Color { get; set; } = "#8B5CF6";
    private string Icon { get; set; } = "";

    private readonly string[] _presetColors = ["#8B5CF6", "#F59E0B", "#EF4444", "#0EA5E9", "#00FF9D"];

    protected override void OnParametersSet()
    {
        Name = InitialName ?? "";
        Color = InitialColor;
        Icon = InitialIcon ?? "";
    }

    private void Cancel() => MudDialog.Cancel();

    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Color)) return;
        MudDialog.Close(DialogResult.Ok(new CategoryDialogResult
        {
            Name = Name,
            Color = Color,
            Icon = Icon
        }));
    }
}
```

- [ ] **Step 3: Create CategoryDialogResult.cs**

```csharp
// TodoList.Web/Client/Components/CategoryDialogResult.cs
namespace TodoList.Web.Client.Components;

public record CategoryDialogResult
{
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#8B5CF6";
    public string Icon { get; init; } = "";
}
```

- [ ] **Step 4: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 5: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Components/CategoryCard.razor TodoList.Web/Client/Components/CategoryDialog.razor TodoList.Web/Client/Components/CategoryDialogResult.cs
git commit -m "feat: add CategoryCard and CategoryDialog components"
```

---

### Task 8: Todo List page

**Files:**
- Create: `TodoList.Web/Client/Pages/TodoList.razor`

- [ ] **Step 1: Create TodoList.razor**

```razor
@* TodoList.Web/Client/Pages/TodoList.razor *@
@page "/"
@using TodoList.Domain.ReadModels
@inject ILocalTodoStore TodoStore
@inject ILocalCategoryStore CategoryStore
@inject IDialogService DialogService
@inject ISnackbar Snackbar
@implements IDisposable

<PageTitle>Todo List</PageTitle>

<ConnectivityBanner Show="false" />

<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:1.5rem;">
    <MudText Typo="Typo.h5" Style="font-family:var(--font-heading);color:var(--text-main);font-weight:600;">
        My Tasks
    </MudText>
    <MudText Typo="Typo.body2" Style="color:var(--text-muted);">
        @_activeTodos.Count active · @_completedTodos.Count done
    </MudText>
</div>

@* Active tasks section *@
<MudPaper Elevation="0"
          Style="background:var(--bg-surface);border-radius:var(--radius);margin-bottom:1rem;">
    <div style="padding:0.75rem 1rem;border-bottom:1px solid var(--border);">
        <MudText Typo="Typo.overline" Style="color:var(--text-muted);font-family:var(--font-mono);font-size:0.65rem;letter-spacing:0.1em;">
            ACTIVE TASKS (@_activeTodos.Count)
        </MudText>
    </div>
    @if (_activeTodos.Count == 0)
    {
        <div style="padding:2rem;text-align:center;">
            <MudText Typo="Typo.body2" Style="color:var(--text-muted);">
                No active tasks. Add one below.
            </MudText>
        </div>
    }
    else
    {
        <MudList T="string" Padding="false">
            @foreach (var todo in _activeTodos)
            {
                <TaskRow Todo="@todo"
                         IsUnsynced="false"
                         IsConflicted="false"
                         OnCompletedChanged="@((completed) => HandleComplete(todo, completed))"
                         OnDelete="@(() => HandleDelete(todo))"
                         OnEdit="@(() => HandleEdit(todo))" />
            }
        </MudList>
    }
</MudPaper>

@* Completed tasks section — collapsed by default *@
@if (_completedTodos.Count > 0)
{
    <MudExpansionPanel Style="background:var(--bg-surface);border-radius:var(--radius);" Elevation="0">
        <TitleContent>
            <MudText Typo="Typo.overline" Style="color:var(--text-muted);font-family:var(--font-mono);font-size:0.65rem;letter-spacing:0.1em;">
                COMPLETED (@_completedTodos.Count)
            </MudText>
        </TitleContent>
        <ChildContent>
            <MudList T="string" Padding="false">
                @foreach (var todo in _completedTodos)
                {
                    <TaskRow Todo="@todo"
                             IsUnsynced="false"
                             IsConflicted="false"
                             OnCompletedChanged="@((completed) => HandleComplete(todo, completed))"
                             OnDelete="@(() => HandleDelete(todo))"
                             OnEdit="@(() => HandleEdit(todo))" />
                }
            </MudList>
        </ChildContent>
    </MudExpansionPanel>
}

@* FAB — Add task *@
<MudFab Color="Color.Primary"
        StartIcon="@Icons.Material.Filled.Add"
        Style="position:fixed;bottom:2rem;right:2rem;z-index:100;"
        OnClick="OpenAddDialog" />

@code {
    private List<TodoSummary> _activeTodos = [];
    private List<TodoSummary> _completedTodos = [];

    protected override void OnInitialized()
    {
        TodoStore.OnChange += RefreshLists;
        RefreshLists();
    }

    private void RefreshLists()
    {
        _activeTodos = TodoStore.Todos.Where(t => !t.IsCompleted)
            .OrderBy(t => t.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.CreatedAt)
            .ToList();
        _completedTodos = TodoStore.Todos.Where(t => t.IsCompleted)
            .OrderByDescending(t => t.CompletedAt)
            .ToList();
        InvokeAsync(StateHasChanged);
    }

    private async Task OpenAddDialog()
    {
        var parameters = new DialogParameters<TaskDialog>
        {
            { x => x.IsEdit, false },
            { x => x.Categories, CategoryStore.Categories }
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<TaskDialog>("Add Task", parameters, options);
        var result = await dialog.Result;
        if (!result.Canceled && result.Data is TaskDialogResult data)
        {
            // Plan C will dispatch CreateTodoCommand here
            Snackbar.Add($"'{data.Title}' added (not yet wired to API)", Severity.Info);
        }
    }

    private async Task HandleEdit(TodoSummary todo)
    {
        var parameters = new DialogParameters<TaskDialog>
        {
            { x => x.IsEdit, true },
            { x => x.InitialTitle, todo.Title },
            { x => x.InitialCategoryId, todo.CategoryId },
            { x => x.InitialDueDate, todo.DueDate?.DateTime },
            { x => x.InitialProgress, todo.Progress },
            { x => x.Categories, CategoryStore.Categories }
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<TaskDialog>("Edit Task", parameters, options);
        var result = await dialog.Result;
        if (!result.Canceled && result.Data is TaskDialogResult data)
        {
            // Plan C will dispatch update commands
            Snackbar.Add($"'{data.Title}' updated (not yet wired to API)", Severity.Info);
        }
    }

    private void HandleComplete(TodoSummary todo, bool completed)
    {
        // Plan C will dispatch CompleteTodoCommand / unimplemented for now
        Snackbar.Add($"'{todo.Title}' {(completed ? "completed" : "uncompleted")} (not yet wired to API)", Severity.Info);
    }

    private void HandleDelete(TodoSummary todo)
    {
        // Plan C will dispatch DeleteTodoCommand
        Snackbar.Add($"'{todo.Title}' deleted (not yet wired to API)", Severity.Warning);
    }

    public void Dispose() => TodoStore.OnChange -= RefreshLists;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Pages/TodoList.razor
git commit -m "feat: add Todo List page with active/completed sections and Add Task FAB"
```

---

### Task 9: Task Detail page

**Files:**
- Create: `TodoList.Web/Client/Pages/TodoDetail.razor`

- [ ] **Step 1: Create TodoDetail.razor**

```razor
@* TodoList.Web/Client/Pages/TodoDetail.razor *@
@page "/todos/{Id}"
@using TodoList.Domain.ReadModels
@inject ILocalTodoStore TodoStore
@inject ILocalCategoryStore CategoryStore
@inject ISnackbar Snackbar
@inject NavigationManager Nav
@implements IDisposable

<PageTitle>@(_todo?.Title ?? "Task")</PageTitle>

@if (_todo == null)
{
    <div style="display:flex;justify-content:center;padding:3rem;">
        <MudText Style="color:var(--text-muted);">Task not found.</MudText>
    </div>
}
else
{
    <div style="max-width:640px;">
        <div style="display:flex;align-items:center;gap:0.75rem;margin-bottom:1.5rem;">
            <MudIconButton Icon="@Icons.Material.Filled.ArrowBack"
                           OnClick="@(() => Nav.NavigateTo("/"))"
                           Style="color:var(--text-muted);" />
            <MudText Typo="Typo.h6" Style="font-family:var(--font-heading);">
                Task Detail
            </MudText>
        </div>

        <MudPaper Elevation="0" Style="background:var(--bg-surface);border-radius:var(--radius);padding:1.5rem;">
            <MudStack Spacing="3">
                <MudTextField @bind-Value="_title"
                              Label="Title"
                              Variant="Variant.Outlined"
                              @onblur="SaveTitle"
                              Immediate="true" />

                <MudSelect T="string"
                           @bind-Value="_categoryId"
                           Label="Category"
                           Variant="Variant.Outlined"
                           Clearable="true"
                           ValueChanged="SaveCategory">
                    @foreach (var cat in CategoryStore.Categories)
                    {
                        <MudSelectItem T="string" Value="@cat.Id">
                            <div style="display:flex;align-items:center;gap:0.5rem;">
                                <span style="@($"width:10px;height:10px;border-radius:2px;background:{cat.Color};display:inline-block;")"></span>
                                @cat.Name
                            </div>
                        </MudSelectItem>
                    }
                </MudSelect>

                <MudDatePicker @bind-Date="_dueDate"
                               Label="Due date"
                               Variant="Variant.Outlined"
                               Clearable="true"
                               DateChanged="SaveDueDate" />

                <MudTextField @bind-Value="_notes"
                              Label="Notes"
                              Variant="Variant.Outlined"
                              Lines="4"
                              @onblur="SaveNotes"
                              Immediate="true" />

                <MudSlider @bind-Value="_progress"
                           Min="0"
                           Max="100"
                           Step="5"
                           Color="Color.Primary"
                           ValueChanged="SaveProgress">
                    Progress: @_progress%
                </MudSlider>

                <div style="display:flex;justify-content:space-between;align-items:center;padding-top:0.5rem;border-top:1px solid var(--border);">
                    <MudText Typo="Typo.caption" Style="color:var(--text-muted);">
                        Created @_todo.CreatedAt.ToString("MMM d, yyyy")
                    </MudText>
                    <MudButton Variant="Variant.Outlined"
                               Color="Color.Error"
                               Size="Size.Small"
                               StartIcon="@Icons.Material.Filled.Delete"
                               OnClick="HandleDelete">
                        Delete
                    </MudButton>
                </div>
            </MudStack>
        </MudPaper>
    </div>
}

@code {
    [Parameter] public string Id { get; set; } = "";

    private TodoSummary? _todo;
    private string _title = "";
    private string? _categoryId;
    private DateTime? _dueDate;
    private string? _notes;
    private int _progress;

    protected override void OnInitialized()
    {
        TodoStore.OnChange += Reload;
        Reload();
    }

    protected override void OnParametersSet() => Reload();

    private void Reload()
    {
        _todo = TodoStore.GetById(Id);
        if (_todo != null)
        {
            _title = _todo.Title;
            _categoryId = _todo.CategoryId;
            _dueDate = _todo.DueDate?.DateTime;
            _notes = null; // Notes not in summary — would be fetched separately in real impl
            _progress = _todo.Progress;
        }
        InvokeAsync(StateHasChanged);
    }

    private void SaveTitle()
    {
        if (_todo == null || _title == _todo.Title) return;
        // Plan C dispatches RenameTodoCommand
        Snackbar.Add("Title saved (not yet wired to API)", Severity.Info);
    }

    private void SaveCategory(string? value)
    {
        // Plan C dispatches AssignCategoryCommand / UnassignCategoryCommand
        Snackbar.Add("Category saved (not yet wired to API)", Severity.Info);
    }

    private void SaveDueDate(DateTime? value)
    {
        // Plan C dispatches SetDueDateCommand / ClearDueDateCommand
        Snackbar.Add("Due date saved (not yet wired to API)", Severity.Info);
    }

    private void SaveNotes()
    {
        // Plan C dispatches UpdateNotesCommand
        Snackbar.Add("Notes saved (not yet wired to API)", Severity.Info);
    }

    private void SaveProgress(int value)
    {
        _progress = value;
        // Plan C dispatches UpdateProgressCommand
        Snackbar.Add("Progress saved (not yet wired to API)", Severity.Info);
    }

    private void HandleDelete()
    {
        // Plan C dispatches DeleteTodoCommand then navigates back
        Nav.NavigateTo("/");
    }

    public void Dispose() => TodoStore.OnChange -= Reload;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Pages/TodoDetail.razor
git commit -m "feat: add Task Detail page (auto-saves on blur, mobile full-page route)"
```

---

### Task 10: Categories page

**Files:**
- Create: `TodoList.Web/Client/Pages/Categories.razor`

- [ ] **Step 1: Create Categories.razor**

```razor
@* TodoList.Web/Client/Pages/Categories.razor *@
@page "/categories"
@using TodoList.Domain.ReadModels
@inject ILocalCategoryStore CategoryStore
@inject IDialogService DialogService
@inject ISnackbar Snackbar
@implements IDisposable

<PageTitle>Categories</PageTitle>

<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:1.5rem;">
    <MudText Typo="Typo.h5" Style="font-family:var(--font-heading);color:var(--text-main);font-weight:600;">
        Categories
    </MudText>
    <MudText Typo="Typo.body2" Style="color:var(--text-muted);">
        @CategoryStore.Categories.Count categories
    </MudText>
</div>

<MudGrid Spacing="2">
    @foreach (var cat in CategoryStore.Categories.OrderBy(c => c.Order))
    {
        <MudItem xs="12" sm="6" md="4" lg="3">
            <CategoryCard Category="@cat"
                          OnEdit="@(() => OpenEditDialog(cat))"
                          OnDelete="@(() => HandleDelete(cat))"
                          OnCardClick="@(() => {})" />
        </MudItem>
    }

    @* New Category card *@
    <MudItem xs="12" sm="6" md="4" lg="3">
        <MudCard Style="background:transparent;border-radius:var(--radius);border:2px dashed var(--border);cursor:pointer;transition:border-color var(--transition);"
                 @onclick="OpenCreateDialog"
                 @onmouseenter="() => _newCardHover = true"
                 @onmouseleave="() => _newCardHover = false"
                 Elevation="0"
                 Class="@(_newCardHover ? "new-category-card-hover" : "")">
            <MudCardContent Style="padding:1rem;display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:80px;gap:0.5rem;">
                <MudIcon Icon="@Icons.Material.Filled.Add"
                         Style="@($"color:{(_newCardHover ? "var(--primary)" : "var(--text-muted)")};transition:color var(--transition);")"
                         Size="Size.Large" />
                <MudText Typo="Typo.body2"
                         Style="@($"color:{(_newCardHover ? "var(--primary)" : "var(--text-muted)")};transition:color var(--transition);")">
                    New Category
                </MudText>
            </MudCardContent>
        </MudCard>
    </MudItem>
</MudGrid>

@code {
    private bool _newCardHover;

    protected override void OnInitialized() => CategoryStore.OnChange += StateHasChanged;

    private async Task OpenCreateDialog()
    {
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
        var dialog = await DialogService.ShowAsync<CategoryDialog>("New Category", new DialogParameters<CategoryDialog>
        {
            { x => x.IsEdit, false }
        }, options);
        var result = await dialog.Result;
        if (!result.Canceled && result.Data is CategoryDialogResult data)
        {
            Snackbar.Add($"'{data.Name}' created (not yet wired to API)", Severity.Info);
        }
    }

    private async Task OpenEditDialog(CategorySummary cat)
    {
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
        var dialog = await DialogService.ShowAsync<CategoryDialog>("Edit Category", new DialogParameters<CategoryDialog>
        {
            { x => x.IsEdit, true },
            { x => x.InitialName, cat.Name },
            { x => x.InitialColor, cat.Color },
            { x => x.InitialIcon, cat.Icon }
        }, options);
        var result = await dialog.Result;
        if (!result.Canceled && result.Data is CategoryDialogResult data)
        {
            Snackbar.Add($"'{data.Name}' updated (not yet wired to API)", Severity.Info);
        }
    }

    private void HandleDelete(CategorySummary cat)
    {
        Snackbar.Add($"'{cat.Name}' deleted (not yet wired to API)", Severity.Warning);
    }

    public void Dispose() => CategoryStore.OnChange -= StateHasChanged;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 3: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Pages/Categories.razor
git commit -m "feat: add Categories page with responsive grid, CategoryCard, and create/edit dialogs"
```

---

### Task 11: Login page

**Files:**
- Create: `TodoList.Web/Client/Pages/Login.razor`

- [ ] **Step 1: Create Login.razor**

```razor
@* TodoList.Web/Client/Pages/Login.razor *@
@page "/login"
@layout EmptyLayout

<PageTitle>Sign in — TodoList</PageTitle>

<div style="display:flex;justify-content:center;align-items:center;min-height:100vh;background:var(--bg-app);padding:1rem;">
    <MudPaper Elevation="0"
              Style="background:var(--bg-surface);border-radius:var(--radius);padding:2.5rem;width:100%;max-width:380px;text-align:center;">

        <MudText Typo="Typo.h4" Style="font-family:var(--font-heading);color:var(--primary);font-weight:700;margin-bottom:0.25rem;">
            TodoList
        </MudText>
        <MudText Typo="Typo.body1" Style="color:var(--text-muted);margin-bottom:2rem;">
            Sign in to your account
        </MudText>

        <MudStack Spacing="2">
            <MudButton Variant="Variant.Outlined"
                       FullWidth="true"
                       StartIcon="@Icons.Custom.Brands.Google"
                       Style="border-color:var(--border);color:var(--text-main);justify-content:flex-start;padding:0.75rem 1rem;"
                       Href="/auth/google">
                Continue with Google
            </MudButton>

            <MudButton Variant="Variant.Outlined"
                       FullWidth="true"
                       StartIcon="@Icons.Custom.Brands.GitHub"
                       Style="border-color:var(--border);color:var(--text-main);justify-content:flex-start;padding:0.75rem 1rem;"
                       Href="/auth/github">
                Continue with GitHub
            </MudButton>
        </MudStack>

        <MudText Typo="Typo.caption" Style="color:var(--text-muted);margin-top:2rem;display:block;">
            By signing in, you agree to the terms of service.
        </MudText>
    </MudPaper>
</div>
```

The login page uses `@layout EmptyLayout` to render outside the main shell. Create a minimal empty layout:

```razor
@* TodoList.Web/Client/Layout/EmptyLayout.razor *@
@inherits LayoutComponentBase

@Body
```

- [ ] **Step 2: Create EmptyLayout**

Create file `TodoList.Web/Client/Layout/EmptyLayout.razor` with content shown in Step 1.

- [ ] **Step 3: Build**

```bash
dotnet build TodoList.Web/Client/TodoList.Web.Client.csproj
```

- [ ] **Step 4: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Web/Client/Pages/Login.razor TodoList.Web/Client/Layout/EmptyLayout.razor
git commit -m "feat: add Login page with Google/GitHub OAuth buttons (outside main shell)"
```

---

### Task 12: Service worker + AppHost update + final wiring

**Files:**
- Create: `TodoList.Web/Client/wwwroot/service-worker.js`
- Create: `TodoList.Web/Client/wwwroot/service-worker.published.js`
- Modify: `TodoList.AppHost/TodoList.AppHost.csproj`
- Modify: `TodoList.AppHost/Program.cs`

- [ ] **Step 1: Create service-worker.js (development)**

```javascript
// TodoList.Web/Client/wwwroot/service-worker.js
// Development service worker — no caching, just registers successfully
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => {});
```

- [ ] **Step 2: Create service-worker.published.js (production)**

```javascript
// TodoList.Web/Client/wwwroot/service-worker.published.js
// Production service worker — cache-first for app shell, network-only for API
importScripts('./service-worker-assets.js');

const CACHE_NAME = `todolist-v${self.assetsManifest.version}`;
const APP_SHELL = self.assetsManifest.assets.map(a => a.url);

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.addAll(APP_SHELL))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(
                keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k))
            ))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);

    // Network-only for API and auth — never cache these
    if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/auth/')) {
        return; // falls through to network
    }

    // Cache-first for app shell assets
    event.respondWith(
        caches.match(event.request)
            .then(cached => cached ?? fetch(event.request))
    );
});
```

- [ ] **Step 3: Verify AppHost references the Server project**

Read `TodoList.AppHost/TodoList.AppHost.csproj` and `TodoList.AppHost/Program.cs`. If the AppHost still references the old `TodoList.Web.csproj`, update it:

In `TodoList.AppHost.csproj`, change:
```xml
<ProjectReference Include="..\TodoList.Web\TodoList.Web.csproj" />
```
to:
```xml
<ProjectReference Include="..\TodoList.Web\Server\TodoList.Web.Server.csproj" />
```

In `TodoList.AppHost/Program.cs`, the project resource type reference may need updating. Read the file and update `Projects.TodoList_Web` → `Projects.TodoList_Web_Server` if the old name is used.

- [ ] **Step 4: Build the full solution**

```bash
cd /Users/jim/code/todo-patterns
dotnet build
```

Expected: Full solution builds. All projects — Api, Web.Server, Web.Client, AppHost, etc. — compile without errors.

If there are errors in Web.Client related to missing `TodoList.Domain` types, temporarily add the inline stub file from Task 3 Step 4 and note it must be removed after Plan A runs.

- [ ] **Step 5: Final commit**

```bash
cd /Users/jim/code/todo-patterns
git add .
git commit -m "feat: complete Blazor WASM Web UI — shell, screens, components, PWA service worker"
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|---|---|
| Blazor WASM hosted model, Server + Client split | Task 1, 2 |
| MudBlazor theme with Stitch design tokens | Task 3 |
| CSS custom properties | Task 2 step 6 |
| Space Grotesk / IBM Plex Sans / IBM Plex Mono fonts | Task 2 step 5 |
| ILocalTodoStore / ILocalCategoryStore interfaces | Task 3 |
| Shell: persistent drawer desktop, bottom nav mobile | Task 4 |
| Login page outside shell | Task 11 |
| Todo List page — Active / Completed sections | Task 8 |
| Task row: checkbox, category chip, due date, progress bar | Task 6 |
| Unsynced dot | Task 5 |
| Conflict warning | Task 5 |
| Connectivity banner | Task 5 |
| Add Task FAB → TaskDialog | Task 8 |
| Task Detail page — mobile full page | Task 9 |
| Categories page — responsive grid | Task 10 |
| CategoryCard with color strip | Task 7 |
| CategoryDialog with color picker | Task 7 |
| PWA manifest | Task 2 step 7 |
| Service worker — cache-first app shell, network-only API | Task 12 |
| AuthController — /auth/google, /auth/github, /auth/logout, /api/me | Task 1 |
| Google + GitHub OAuth | Task 1 |
| AppHost wiring | Task 12 |

**Placeholder scan:** Steps that can't wire to real API yet say "not yet wired to API" in snackbar text — this is intentional, not a placeholder. Plan C provides real CommandDispatcher wiring.

**Type consistency:** `TodoSummary`, `CategorySummary` used consistently throughout. `TaskDialogResult`, `CategoryDialogResult` defined before use. `ILocalTodoStore`/`ILocalCategoryStore` defined in Task 3, consumed in Tasks 4, 8, 9, 10.
