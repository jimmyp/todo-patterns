# Web UI Design

**Date:** 2026-04-07
**Status:** Approved
**Purpose:** Define the Blazor WebAssembly + MudBlazor Web UI — a mobile-first PWA that is also distributed as a native offline-capable app. Visual design derived from the Stitch "Clean Dark Foundation" design system.

---

## 1. Tech Stack

| Concern | Technology |
|---|---|
| Framework | Blazor WebAssembly (.NET 10) |
| Component library | MudBlazor (latest stable) |
| Hosting | Blazor WASM hosted — served by `TodoList.Web` ASP.NET Core host |
| PWA | Blazor PWA template (`service-worker.published.js`) |
| CSS | MudBlazor theme + CSS custom properties for design tokens |
| Icons | Google Material Symbols (via font) |

The `TodoList.Web` project replaces the current Razor Pages stub. It becomes a Blazor WASM hosted project: a thin ASP.NET Core host serving the WASM app, handling OAuth login/logout, and proxying auth cookies.

---

## 2. Design System

Design tokens derived from the Stitch "Clean Dark Foundation" screens. Defined as CSS custom properties and mapped into a custom `MudTheme`.

### Color tokens

| Token | Value | Usage |
|---|---|---|
| `--bg-app` | `#0B0D10` | Page background |
| `--bg-surface` | `#161920` | Cards, sidebar, dialogs |
| `--bg-surface-hover` | `#1F242D` | Row/card hover state |
| `--text-main` | `#E2E8F0` | Primary text |
| `--text-muted` | `#8492A6` | Secondary text, metadata |
| `--border` | `#1F242D` | Borders |
| `--primary` | `#8B5CF6` | Accent, FAB, active states |
| `--success` | `#00FF9D` | Completed state |
| `--error` | `#EF4444` | Overdue, destructive actions |
| `--warning` | `#F59E0B` | Warnings |

### Typography

| Role | Font | Notes |
|---|---|---|
| Headings | Space Grotesk | Loaded via Google Fonts |
| Body | IBM Plex Sans | Loaded via Google Fonts |
| Badges / metadata | IBM Plex Mono | Monospace, uppercase |

### Geometry

- Border radius: `4px` throughout (tight/square aesthetic)
- Transitions: `150ms cubic-bezier(0.4, 0, 0.2, 1)`

---

## 3. Shell Layout

### Desktop (≥ 960px)

```
┌──────────────────────────────────────────────┐
│ [Sidebar 240px] │ [Top bar]                  │
│                 │ [Main content, max-w 4xl]  │
│  Logo           │                            │
│  Nav links      │                            │
│  ...            │                            │
│  [User profile] │                            │
└──────────────────────────────────────────────┘
```

- `MudDrawer` variant `Persistent`, width `240px`
- Active nav item: `border-l-2` left accent in `--primary`, `bg-surface-hover`
- User profile strip pinned to drawer bottom: avatar, display name, logout link

### Mobile (< 960px)

- `MudAppBar` top: logo + hamburger (opens `MudDrawer` as temporary overlay)
- `MudBottomNavigation` bottom: Todo List, Categories tabs
- `MudFab` fixed bottom-right (above bottom nav)

---

## 4. Screens

### Todo List (`/`)

- Two sections: **Active Tasks** and **Completed**
- Each task row (`MudListItem`):
  - Checkbox (`MudCheckBox`) — checking triggers `CompleteTodo` command
  - Title — strikethrough + 60% opacity when completed
  - Category `MudChip` — colored to match category color, monospace uppercase text
  - Due date — monospace; red + overdue icon (`priority_high`) if past due
  - `MudProgressLinear` — shown only if `progress > 0`
  - Unsynced dot — subtle `--text-muted` dot indicator when command is speculative (offline)
  - Row hover reveals `more_vert` overflow `MudMenu` (edit, delete)
- Completed rows: 60% opacity, green (`--success`) checkbox fill
- `MudFab` (bottom-right): opens **Add Task** `MudDialog`

### Add Task dialog

`MudDialog` with:
- `MudTextField` — title (required)
- `MudSelect` — category (optional, shows color swatch + name)
- `MudDatePicker` — due date (optional)
- `MudTextField` multiline — notes (optional)
- `MudSlider` — progress 0–100 (optional, default 0)

### Task Detail

- Desktop: `MudDrawer` right panel (slides in when row clicked)
- Mobile: full-page route `/todos/{id}`
- Same fields as Add Task dialog, pre-populated, auto-saves on blur

### Categories (`/categories`)

- `MudGrid`: 1 col (mobile) → 2–4 col (desktop)
- Each `MudCard`:
  - 4px left-edge colored strip (category color)
  - Material Symbol icon + name
  - Task count badge (monospace, `--bg-surface-hover` background)
  - Hover: `border-primary` border
- "New Category" card: dashed border, centered add icon, opens create `MudDialog`
- Create/Edit `MudDialog`: name, color picker (5 preset swatches + hex input), icon picker (Material Symbols name input)

### Login (`/login`)

- Outside shell — no sidebar/nav
- Centered card on `--bg-app`: logo, "Sign in to TodoList"
- `MudButton` Google OAuth → `/auth/google`
- `MudButton` GitHub OAuth → `/auth/github`

---

## 5. Component Structure

```
TodoList.Web/
  Client/                          # Blazor WASM project
    App.razor
    Layout/
      MainLayout.razor             # Shell: MudLayout + drawer + top bar
      NavMenu.razor                # Sidebar nav links
      UserProfileStrip.razor       # Bottom of drawer
    Pages/
      TodoList.razor               # /
      TodoDetail.razor             # /todos/{id}
      Categories.razor             # /categories
      Login.razor                  # /login
    Components/
      TaskRow.razor
      TaskDialog.razor             # Add/edit dialog
      CategoryCard.razor
      CategoryDialog.razor
      UnsyncedDot.razor
      ConnectivityBanner.razor     # Shown only when sync fails (persistent alert)
    Theme/
      AppTheme.cs                  # MudTheme definition
      DesignTokens.cs              # CSS custom property names as constants
  Server/                          # ASP.NET Core host
    Program.cs                     # Auth, static files, fallback routing
    Controllers/
      AuthController.cs            # /auth/google, /auth/github, /auth/logout
```

---

## 6. Authentication Flow

- Login: browser navigates to `/auth/google` or `/auth/github` → ASP.NET Core Identity OAuth → cookie set → redirect to `/`
- Blazor WASM calls `/api/me` on startup to get current user identity (name, avatar URL)
- Logout: `MudIconButton` in user profile strip → POST `/auth/logout` → cookie cleared → redirect to `/login`
- Unauthenticated: `/api/me` returns 401 → Blazor router redirects to `/login`
