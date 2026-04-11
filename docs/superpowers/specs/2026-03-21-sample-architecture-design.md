# Sample Reference Architecture Design

**Date:** 2026-03-21
**Status:** Approved
**Purpose:** Define a production-quality .NET 8 reference architecture using a todo list application as the vehicle. All patterns, tooling, and conventions established here are intended to be reused as the foundation for more complex future projects.

---

## Table of Contents

1. [Goals](#1-goals)
2. [Project Structure](#2-project-structure)
3. [Tech Stack](#3-tech-stack)
4. [Authentication](#4-authentication)
5. [API Design](#5-api-design)
6. [MCP.Tools — Standard MCP Interface](#6-mcptools--standard-mcp-interface)
7. [MCP.Composite — Plan/Execute Interface](#7-mcpcomposite--planexecute-interface)
8. [Domain Model](#8-domain-model)
9. [Messaging — Wolverine](#9-messaging--wolverine)
10. [Testing Strategy](#10-testing-strategy)
11. [Observability](#11-observability)
12. [Database Migration Strategy](#12-database-migration-strategy)
13. [CI/CD Pipeline](#13-cicd-pipeline)
14. [Deployment — Azure Container Apps](#14-deployment--azure-container-apps)
15. [Backup and Recovery](#15-backup-and-recovery)
16. [Infrastructure as Code](#16-infrastructure-as-code)
17. [Agent Workflow](#17-agent-workflow)

---

## 1. Goals

This architecture prioritises:

- **In-memory unit tests** as the primary TDD loop — fast, no I/O, runnable on every change
- **Integration tests for all third-party integrations** — real SQL, real transports, real HTTP stubs
- **Fully automated deployment on every commit** with a high number of safety gates
- **Machine-readable feedback at every stage** — structured output agents can consume without human intermediation
- **Agent-native interfaces** — the system is queryable and operable by AI agents without requiring human presence
- **Observable user value** — business metrics alongside technical metrics so agents can verify deployments improved, not just succeeded
- **Zero-setup developer environment** — a dev container provides the full environment (Docker-in-Docker, .NET SDK, Aspire workload) on first open. No local Docker installation required on the host machine.

---

## 2. Project Structure

```
TodoList.sln
├── TodoList.Domain/           # Shared — aggregates, events, commands, read models, validation, saga definitions
├── TodoList.Api/              # REST API — EF Core, Wolverine, SendGrid, SignalR hub
├── TodoList.Web/              # Blazor WASM PWA — mobile-first, offline-capable, references Domain
├── TodoList.Mcp.Tools/        # Standard MCP server — official Anthropic SDK, one tool per capability
├── TodoList.Mcp.Composite/    # Composite API — Plan/Execute endpoints, wraps Api
├── TodoList.AppHost/          # .NET Aspire orchestration — wires all services locally
├── TodoList.ServiceDefaults/  # Shared — OTel, health checks, resilience policies
├── TodoList.Tests/            # Unit tests — fast, in-memory, no I/O
└── TodoList.IntegrationTests/ # Integration tests — Testcontainers, real transports
```

### Responsibility boundaries

| Project | Owns | Does NOT own |
|---|---|---|
| `Domain` | Aggregates, events, commands, read model shapes, validation rules, saga definitions (`ISagaDefinition`) | Persistence, transport, UI |
| `Api` | EF Core persistence, Wolverine sagas, SendGrid email, SignalR hub, HTTP endpoints | Domain logic (delegates to Domain) |
| `Web` | Blazor WASM PWA, MudBlazor UI, ClientStore, local projections, command dispatch | Domain logic (uses Domain directly) |
| `Mcp.Tools` | Standard MCP tools (official SDK), one tool per capability | Domain logic (delegates to Api) |
| `Mcp.Composite` | Plan/Execute endpoints, `$result[N]` chaining | Domain logic (delegates to Api) |
| `AppHost` | Local service wiring, resource definitions | Any runtime logic |
| `ServiceDefaults` | OTel registration, health check conventions, resilience | App-specific code |
| `Tests` | Unit coverage of all domain logic and message handlers | Any real I/O |
| `IntegrationTests` | End-to-end API + infrastructure verification | Unit-level coverage |

### Rationale for separate projects

- `Domain` has no server-side dependencies (no EF Core, no Wolverine, no ASP.NET) — safe to reference from Blazor WASM
- `Api`, `Web`, `Mcp.Tools`, `Mcp.Composite` are separate deployables — they run as independent Container Apps
- Agents choose which agent-native interface to install: `Mcp.Tools` for interactive/conversational use (Claude Desktop, Claude Code), `Mcp.Composite` for agentic loops that want to minimise round trips
- `AppHost` is a hard requirement of .NET Aspire
- `ServiceDefaults` prevents OTel and health check wiring being duplicated across three service projects
- `IntegrationTests` is separate from `Tests` so CI can run unit tests fast first (< 2 seconds) and integration tests separately (requires Docker)

---

## 3. Tech Stack

| Concern | Technology | Version |
|---|---|---|
| Runtime | .NET | 10.0 |
| Web framework | ASP.NET Core Razor Pages | 10.0 |
| API framework | ASP.NET Core Minimal API | 10.0 |
| ORM | Entity Framework Core | 10.0 |
| Database | Azure SQL (production), SQL Server container (local/CI) | — |
| Messaging | Wolverine | Latest stable |
| Message transport | Azure Service Bus (production), in-memory (tests) | — |
| Email | SendGrid via Wolverine handler | — |
| Local orchestration | .NET Aspire | Latest stable |
| Observability | OpenTelemetry SDK | Latest stable |
| Local OTel UI | Aspire Dashboard | (bundled with AppHost) |
| Production OTel | Azure Monitor / Application Insights | — |
| Unit test framework | xUnit | 2.x |
| Integration test infra | Testcontainers for .NET | Latest stable |
| HTTP stubbing | WireMock.NET | Latest stable |
| CLI generation | Kiota (Microsoft) | Latest stable |
| Authentication | ASP.NET Core Identity | 10.0 |
| Dev environment | Dev Container (Docker-in-Docker) | — |
| Social login | Google OAuth 2.0, GitHub OAuth | — |
| IaC (core) | Bicep via .NET Aspire + azd | — |
| IaC (overlay) | Pulumi C# | Latest stable |
| CI | GitHub Actions | — |
| Deployment orchestration | Octopus Deploy (Cloud/SaaS) | — |
| Hosting | Azure Container Apps (Consumption plan) | — |

---

## 4. Authentication

### Social login

Users authenticate via Google or GitHub OAuth. ASP.NET Core Identity manages the local user record; the OAuth providers handle credential storage and MFA.

```csharp
builder.Services.AddAuthentication()
    .AddGoogle(options => {
        options.ClientId     = config["Auth:Google:ClientId"];
        options.ClientSecret = config["Auth:Google:ClientSecret"];
    })
    .AddGitHub(options => {
        options.ClientId     = config["Auth:GitHub:ClientId"];
        options.ClientSecret = config["Auth:GitHub:ClientSecret"];
    });
```

On first login, an ASP.NET Core Identity user record is created and linked to the provider claim. Subsequent logins from the same provider update the linked record — no duplicate users.

### Identity storage

ASP.NET Core Identity tables live in the same Azure SQL database as the application data, managed by a separate EF Core `IdentityDbContext`. The schema is created by EF migrations alongside application migrations.

### Agent / service access (API keys)

Human users authenticate via cookie (set by the Web project). Machine callers — agents using `Mcp.Tools`, `Mcp.Composite`, or the raw REST API — authenticate via a pre-shared API key passed as a bearer token:

```
Authorization: Bearer {api-key}
```

API keys are stored as hashed values in the `ApiKeys` table and provisioned via the `todocli` admin surface or directly in the database during infrastructure setup. Keys are scoped to a role (`read`, `write`, `admin`) and expire on a configurable TTL.

### Route protection

- Web project: standard cookie-based `[Authorize]` — unauthenticated users are redirected to the login page
- Api project: `[Authorize]` using either cookie (for browser-initiated calls from Web) or bearer token (for agents and CLI)
- `Mcp.Tools`, `Mcp.Composite`: bearer token only — no browser session involved

---

## 5. API Design

### Async command pattern

All mutating operations follow the async command pattern. The API never makes the caller wait for downstream effects (Service Bus, email).

```
POST   /todos                          → 202 Accepted
                                         Location: /todos/operations/{operationId}
                                         X-Retry-After-Ms: {milliseconds}

GET    /todos/operations/{operationId} → 200 { status, result? }
GET    /todos                          → 200 [...]
GET    /todos/{id}                     → 200 { id, title, isCompleted, createdAt, completedAt? }
POST   /todos/{id}/complete            → 202 + Location + X-Retry-After-Ms
DELETE /todos/{id}                     → 202 + Location + X-Retry-After-Ms
```

**`X-Retry-After-Ms` header:** A custom header (not the standard RFC 7231 `Retry-After`) because we need millisecond precision and RFC 7231 defines `Retry-After` in whole seconds. Using the standard header with millisecond values would cause standard HTTP clients to misinterpret the value by a factor of 1000. The Api queries Service Bus queue depth at response time and returns a recommended polling interval. Clients treat this as a hard instruction, not a suggestion.

```
queue depth 0–10:    200ms
queue depth 11–100:  500ms
queue depth 101+:    2000ms
```

**Operation status lifecycle:**

```
pending → processing → complete
                    ↘ failed { reason, retryable }
```

### OpenAPI + Kiota CLI

The Api publishes an OpenAPI 3.x spec at `/openapi/v1.json`. A build step generates a typed `todocli` binary via Kiota:

```bash
todocli todos list
todocli todos add --title "buy milk"
todocli todos complete --id 42
todocli operations get --id abc123
```

Agents can use this CLI directly without understanding the HTTP schema. The CLI is published as a build artifact on every CI run. Agents and automation workflows that need `todocli` download it from the CI artifact before use:

```bash
gh run download {run-id} --name todocli --dir ./bin
chmod +x ./bin/todocli
```

### Optimistic updates in the Web project

The Web project updates its local UI state immediately on user action without waiting for the operation to reach `complete`. It polls the operation endpoint in the background using the `X-Retry-After-Ms` value and reconciles if the operation fails.

---

## 6. Mcp.Tools — Standard MCP Interface

`TodoList.Mcp.Tools` is a separate deployable that exposes the todo domain via the official [Anthropic MCP SDK](https://github.com/anthropics/anthropic-sdk-dotnet). It is protocol-compliant and works with any MCP-compatible client: Claude Desktop, Claude Code, or any agent that speaks standard MCP.

### Tools exposed

Each capability is a discrete MCP tool with a typed schema. The MCP SDK auto-generates the JSON Schema from C# types:

| Tool | Description |
|---|---|
| `create_todo` | Create a new todo item |
| `list_todos` | List all todos with optional filter |
| `complete_todo` | Mark a todo as complete |
| `delete_todo` | Delete a todo |
| `get_operation` | Poll an async operation by ID |

### Tool pattern

Each tool call maps to one API call. The tool returns the result directly — including the `operationId` from async endpoints, which the client can poll via `get_operation`. One tool call at a time.

```csharp
[McpTool("create_todo", Description = "Create a new todo item")]
public async Task<CreateTodoResult> CreateTodoAsync(
    [McpToolParam("title", Description = "Todo title (required, max 500 chars)")] string title)
{
    var response = await _apiClient.PostAsync("/todos", new { title });
    // returns { id, operationId, retryAfterMs }
}
```

### When to use Mcp.Tools

- Interactive agents: Claude Desktop, Claude Code — where the host manages tool calls step by step
- Agents that prefer one-tool-at-a-time control flow
- Clients that only speak standard MCP protocol

---

## 7. Mcp.Composite — Plan/Execute Interface

`TodoList.Mcp.Composite` is a separate deployable that implements the **Composite API** pattern. It exposes two endpoints (`POST /plan` and `POST /execute`) that allow agents to discover capabilities and then execute multi-step plans in a single round trip.

This is the canonical pattern name from Salesforce's API design. The two-phase approach minimises round trips for agentic loops: discover once, execute as a composed plan.

### `POST /plan`

Agents discover capabilities on demand. Returns structured descriptions, schemas, and examples for a stated intent. Keeps agent context small — load only what you need.

```json
// Request
{ "about": "how do I create and then complete a todo?" }

// Response
{
  "capabilities": [
    {
      "name": "create_todo",
      "description": "Creates a new todo item",
      "parameters": { "title": "string (required)" },
      "returns": "{ id, operationId, retryAfterMs }"
    },
    {
      "name": "complete_todo",
      "description": "Marks a todo as complete",
      "parameters": { "id": "integer (required)" },
      "returns": "{ operationId, retryAfterMs }"
    }
  ],
  "schemas": {
    "todo": { "id": "int", "title": "string", "isCompleted": "bool", "createdAt": "datetime" },
    "operation": { "id": "string", "status": "pending|processing|complete|failed", "result": "object?" }
  },
  "examples": [
    {
      "description": "Create then complete a todo",
      "operations": [
        { "op": "create_todo", "params": { "title": "buy milk" } },
        { "op": "complete_todo", "params": { "id": "$result[0].id" } }
      ]
    }
  ]
}
```

### `POST /execute`

Agents submit a composed plan of operations in a single call. References to earlier results in the same plan are supported via `$result[N].field` syntax. One round trip for a multi-step plan.

```json
// Request
{
  "operations": [
    { "op": "create_todo", "params": { "title": "buy milk" } },
    { "op": "complete_todo", "params": { "id": "$result[0].id" } },
    { "op": "list_todos", "params": {} }
  ]
}

// Response
{
  "results": [
    { "index": 0, "status": "complete", "result": { "id": 42, "operationId": "abc" } },
    { "index": 1, "status": "complete", "result": { "operationId": "def" } },
    { "index": 2, "status": "complete", "result": [ ... ] }
  ],
  "failed": []
}
```

**Error handling for `$result[N].field` references:**

If an upstream operation fails, all downstream operations that reference its result are skipped and appear in `failed` with `reason: "dependency_failed"`. Operations with no dependency on the failed operation continue to execute:

```json
// Request: create_todo (0), complete_todo referencing $result[0].id (1), list_todos (2)
// Operation 0 fails → operation 1 skipped → operation 2 has no dependency and runs

{
  "results": [
    { "index": 2, "status": "complete", "result": [ { "id": 1, "title": "existing todo" } ] }
  ],
  "failed": [
    { "index": 0, "status": "failed", "reason": "validation_error", "detail": "Title is required" },
    { "index": 1, "status": "skipped", "reason": "dependency_failed", "dependency_index": 0 }
  ]
}
```

The plan does not halt on first failure — only dependent operations are skipped. Results appear at their original index regardless of execution order.

### When to use Mcp.Composite

- Agentic loops that want to minimise round trips
- Agents running headless pipelines where every HTTP call has a cost
- Multi-step workflows with known dependencies between operations

---

## 8. Domain Model

### Philosophy

All domain behavior lives on the aggregate. Handlers are pure orchestration: load → call domain method → save → publish events. No business logic in handlers.

### `DomainResult<T>`

Domain methods never throw on invalid transitions. They collect all validation errors and return them without changing state:

```csharp
public sealed class DomainResult<T>
{
    private DomainResult(T value)         { Value = value; Errors = []; }
    private DomainResult(string[] errors) { Value = default; Errors = errors; }
    public T? Value { get; }
    public string[] Errors { get; }
    public bool IsSuccess => Errors.Length == 0;
    public static DomainResult<T> Ok(T value)                  => new(value);
    public static DomainResult<T> Fail(params string[] errors) => new(errors);
}
```

### Aggregate pattern

`Todo.Create` is a static factory method — the only way to produce a valid `Todo`. Instance methods advance state and return the events that resulted from the transition:

```csharp
public static DomainResult<(Todo todo, IReadOnlyList<IDomainEvent> events)> Create(
    string title, DateTimeOffset now)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(title)) errors.Add("Title cannot be empty");
    if (title?.Length > 500)             errors.Add("Title cannot exceed 500 characters");
    if (errors.Count > 0)
        return DomainResult<(Todo, IReadOnlyList<IDomainEvent>)>.Fail([..errors]);

    var todo = new Todo { Id = Guid.NewGuid(), Title = title!.Trim(), CreatedAt = now };
    return DomainResult<(Todo, IReadOnlyList<IDomainEvent>)>.Ok(
        (todo, [new TodoCreatedEvent(todo.Id, todo.Title, now)]));
}

public DomainResult<IReadOnlyList<IDomainEvent>> Complete(DateTimeOffset now)
{
    var errors = new List<string>();
    if (IsDeleted)   errors.Add("Cannot complete a deleted todo");
    if (IsCompleted) errors.Add("Already completed");
    if (errors.Count > 0)
        return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

    IsCompleted = true;
    CompletedAt = now;
    return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoCompletedEvent(Id, now)]);
}

public DomainResult<IReadOnlyList<IDomainEvent>> Uncomplete()
{
    if (!IsCompleted)
        return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Not completed");

    IsCompleted = false;
    CompletedAt = null;
    return DomainResult<IReadOnlyList<IDomainEvent>>.Ok([new TodoUncompletedEvent(Id)]);
}
```

### Handler pattern

Handlers are thin orchestrators. They call the domain method and dispatch the returned events:

```csharp
public async Task Handle(CompleteTodoCommand cmd, IMessageBus bus)
{
    var todo = await _repo.GetAsync(cmd.TodoId);
    var result = todo.Complete(DateTimeOffset.UtcNow);
    if (!result.IsSuccess)
        throw new ValidationException(result.Errors);

    await _repo.SaveAsync(todo);
    foreach (var evt in result.Value!)
        await bus.PublishAsync(evt);
}
```

### Events as first-class values

Domain events are returned from domain methods — not collected as side effects on the aggregate. This makes them directly testable as values. Handlers are responsible for dispatching them via Wolverine.

---

## 9. Messaging — Wolverine

### Message flow

```
TodoService                    Wolverine Bus              Handlers
─────────────────────────────────────────────────────────────────
AddItem()      → publishes →  TodoCreatedEvent   → NotifyHandler → SendGrid
MarkComplete() → publishes →  TodoCompletedEvent → NotifyHandler → SendGrid
DeleteItem()   → publishes →  TodoDeletedEvent   → AuditHandler  → (log only)
```

### Transport per environment

| Environment | Transport | Rationale |
|---|---|---|
| Unit tests | Wolverine in-memory | Zero setup, instant, no infrastructure |
| Integration tests | Wolverine in-memory or Azure SB emulator (Docker) | Real transport shape without credentials |
| Production | Azure Service Bus | Durable, observable, dead-letter support |

### Error handling

Wolverine retries with exponential backoff before moving to the dead-letter queue. Dead-letter messages are visible in Azure Monitor with a full trace showing where they failed. The `/health/detail` endpoint reflects `bus: "degraded"` if dead-letter count exceeds a configurable threshold.

---

## 10. Testing Strategy

### Philosophy

In-memory by default, real infrastructure at the boundary. All domain behavior is exercised through the domain model directly — never through static helpers or handlers.

### Unit tests (`TodoList.Tests/`)

Tests exercise the domain model through valid state transitions. The model can only be reached via its static factory or its own methods — this mirrors real usage and keeps tests refactor-friendly:

```csharp
[Fact]
public void Uncomplete_after_complete_returns_uncompleted_event()
{
    var todo = Todo.Create("buy milk", DateTimeOffset.UtcNow).Value!.todo;
    todo.Complete(DateTimeOffset.UtcNow);

    var result = todo.Uncomplete();

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().ContainSingle().Which.Should().BeOfType<TodoUncompletedEvent>();
    todo.IsCompleted.Should().BeFalse();
}

[Fact]
public void Create_with_empty_title_returns_error_without_changing_state()
{
    var result = Todo.Create("", DateTimeOffset.UtcNow);

    result.IsSuccess.Should().BeFalse();
    result.Errors.Should().Contain("Title cannot be empty");
}

[Fact]
public void Complete_already_completed_todo_returns_error()
{
    var todo = Todo.Create("buy milk", DateTimeOffset.UtcNow).Value!.todo;
    todo.Complete(DateTimeOffset.UtcNow);

    var result = todo.Complete(DateTimeOffset.UtcNow);

    result.IsSuccess.Should().BeFalse();
    result.Errors.Should().Contain("Already completed");
}
```

- No EF Core, no repository — domain model is a pure in-memory object
- Wolverine test harness for message handler routing — no real transport:

```csharp
var session = await Host.InvokeMessageAndWaitAsync(new TodoCreatedEvent(todo));
session.Sent.SingleMessage<SendEmailCommand>().Should().NotBeNull();
```

- WireMock.NET for testing that the right HTTP call is made to SendGrid without real credentials
- All tests run in < 2 seconds
- Agent workflow: `dotnet test --filter Category=Unit` after every change

### Integration tests (`TodoList.IntegrationTests/`)

- `WebApplicationFactory<Program>` boots the full Api HTTP stack
- Testcontainers spins up a real SQL Server container — tests EF migrations, queries, and constraints
- Wolverine test transport (in-process) for message flow tests
- Azure Service Bus emulator (Docker) for full transport integration tests
- WireMock.NET stubs SendGrid HTTP calls — verifies the correct HTTP request shape, response handling, and error scenarios
- Tests run with `dotnet test --filter Category=Integration` — requires Docker

### Test output format

Both test suites emit JUnit XML artifacts consumed by CI. Individual test failures include structured JSON to stderr identifying: test name, category, failure message, stack trace. CI posts this to the GitHub Actions step summary.

### What is NOT tested via mocks

- EF Core is NOT tested with the in-memory provider — it is tested against real SQL Server in integration tests only
- SendGrid is NOT called in any test — WireMock stubs the HTTP boundary in unit tests; the full Wolverine → handler → SendGrid HTTP flow is verified in integration tests

---

## 11. Observability

### Signal types

All four service projects (`Api`, `Web`, `Mcp.Tools`, `Mcp.Composite`) register OTel via `ServiceDefaults`:

- **Traces:** every HTTP request, DB query, Service Bus message, and outbound HTTP call is a span. A single browser click produces a distributed trace: Web → Api → Wolverine → SendGrid
- **Metrics:** request rates, queue depth, DB query duration, operation polling durations, email delivery latency
- **Logs:** structured (not string). Every log entry carries trace and span IDs — logs correlate to traces automatically

### Local (Aspire Dashboard)

AppHost starts the Aspire Dashboard automatically. Developers and agents access live traces, metrics, and logs at `http://localhost:18888`. No setup required.

### Production (Azure Monitor)

OTLP exporter sends all signals to Azure Monitor / Application Insights. Agents query via `az monitor` CLI or the Azure Monitor REST API.

### Health endpoints (on every service project)

```
GET /health/live       → 200 if process is running (liveness probe)
GET /health/ready      → 200 if DB connected, Service Bus reachable, all deps healthy
GET /health/migrations → 200 { backfills: [...], pending_contracts: [...] }
                         Lightweight endpoint for agents and CI to check migration state
                         without the full /health/detail payload.
GET /health/detail     → 200 {
                          version: "1.0.42",
                          build: "abc123",
                          db: "ok",
                          bus: "ok",
                          email: "ok",
                          tables: {
                            Todos: { rows: 62000, size_mb: 12 }
                          },
                          migrations: {
                            pending_contracts: [...],
                            backfills: [...]
                          }
                        }
```

`/health/detail` is the primary signal agents use post-deployment to verify:
1. The correct version landed
2. All dependencies are healthy
3. No migrations are in an unexpected state

### Business metrics (value observability)

Emitted from `TodoService` via `System.Diagnostics.Metrics`:

| Metric | Type | Meaning |
|---|---|---|
| `todos.created` | Counter | User added a todo |
| `todos.completed` | Counter | User completed a todo |
| `todos.deleted` | Counter | User deleted a todo |
| `todos.time_to_completion` | Histogram | Seconds from created → completed |
| `todos.active_count` | Gauge | Current open todos |
| `todos.completion_rate` | Gauge | % of todos that reach completed state |

Every metric carries a `version` dimension (deployed build SHA) and `env` dimension. This enables comparison of user behaviour and value delivery across versions in Azure Monitor:

```
todos.completion_rate { version="abc123", env="production" }
todos.time_to_completion { p50=120s, p95=480s, version="abc123" }
```

If a deployment causes `completion_rate` to drop or `time_to_completion` to spike, the agent knows the deployment degraded user value even if all technical health checks pass.

---

## 12. Database Migration Strategy

### Core principles

- **Forward-only:** no automatic rollback. If a deployment fails, the team moves forward with a fix or a deliberate restore, never an automatic revert.
- **EF Core migrations for DDL only:** schema changes are fast, transactional, and safe. Large data operations never run inside EF migrations.
- **Idempotent DDL by convention:** a custom `SqlServerMigrationsSqlGenerator` wraps all generated DDL in existence guards. Every migration is safe to retry.
- **Dual-write during transition:** while an expand/contract cycle is in flight, the application writes to both old and new columns. Reads come from the old column until the contract phase is complete.

### When expand/contract applies

| Change type | Approach |
|---|---|
| Add column with runtime constant default | Single migration — instant, metadata-only |
| Add column (no default, nullable) | Single migration — instant |
| Rename column / change type / split / merge | Expand/contract — 2 phases |
| Drop unused column (code already not using it) | Contract migration only |

A runtime constant default is any expression that produces the same value for all rows at migration time — a string literal, `GETUTCDATETIME()`, a numeric constant. Adding such a column is a metadata-only operation in SQL Server 2012+ Enterprise and Azure SQL. No table scan, no row locks.

**References:**
- EF Core wraps each migration in a transaction by default. All SQL in `Up()` runs inside one transaction — row locks accumulate. At 5,000 locks, SQL Server escalates to a table-level exclusive lock. `ROWLOCK` hints do not prevent this. Source: [SQL Server lock escalation](https://learn.microsoft.com/troubleshoot/sql/database-engine/performance/resolve-blocking-problems-caused-lock-escalation)
- `suppressTransaction: true` on `migrationBuilder.Sql()` is designed for DDL that databases refuse to run inside a transaction (memory-optimised tables, collation changes) and must be isolated in its own migration. It must NOT be used as a general-purpose performance shortcut for large DML (this would create partial data state and mis-use the primitive). It is acceptable for small, bounded DML backfills (< 10,000 rows) where the purpose is identical: preventing lock accumulation inside the migration transaction. The distinction is bounded vs. unbounded, not DDL vs. DML. Source: [EF Core managing migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/managing). EF Core source confirmation that the `__EFMigrationsHistory` INSERT is also suppressed when any operation uses `suppressTransaction: true`: [MigrationCommandExecutor.cs](https://github.com/dotnet/efcore/blob/main/src/EFCore.Relational/Migrations/Internal/MigrationCommandExecutor.cs)
- EF Core 9 wraps all pending migrations in a single transaction. Source: [EF Core 9 breaking changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-9.0/breaking-changes)
- Microsoft recommends SQL scripts (not `MigrateAsync()` at runtime) for production migrations. Source: [Applying migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying)
- Azure SQL has Optimised Locking enabled by default, which reduces lock escalation risk. It does not eliminate it inside long-running transactions. Source: [Optimised Locking](https://learn.microsoft.com/sql/relational-databases/performance/optimized-locking)

### Idempotent DDL convention

A custom `IdempotentSqlServerMigrationsSqlGenerator` registered in `ServiceDefaults` wraps all generated DDL in existence guards:

```sql
-- AddColumn
IF NOT EXISTS (
  SELECT 1 FROM sys.columns
  WHERE object_id = OBJECT_ID('Todos') AND name = 'CompletedAt'
) ALTER TABLE Todos ADD CompletedAt DATETIME2 NULL;

-- DropColumn
IF EXISTS (
  SELECT 1 FROM sys.columns
  WHERE object_id = OBJECT_ID('Todos') AND name = 'Title'
) ALTER TABLE Todos DROP COLUMN Title;

-- CreateIndex
IF NOT EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE object_id = OBJECT_ID('Todos') AND name = 'IX_Todos_CompletedAt'
) CREATE INDEX IX_Todos_CompletedAt ON Todos(CompletedAt);
```

This protects against the edge case where DDL commits but the `__EFMigrationsHistory` INSERT fails, causing EF to retry a migration whose DDL already ran. This is most relevant for migrations using `suppressTransaction: true` (special DDL that cannot run in a transaction) where the history INSERT also runs outside any transaction.

### Expand/contract phases

#### Phase 1 — Expand

**Migration (transactional):**
```sql
IF NOT EXISTS (...) ALTER TABLE Todos ADD Summary NVARCHAR(200) NULL;
```

**Application code:**
- Dual-writes to both `Title` and `Summary` on every write
- Reads from `Title` only
- App can be rolled forward to this version safely — no data is lost if this deploy has problems because `Title` always has current data

**`lifecycle.json` entry (committed to repo, updated by CI):**
```json
{
  "migration": "Add_Summary_Column",
  "phase": "expand",
  "old_column": "Title",
  "new_column": "Summary",
  "table": "Todos",
  "backfill_goal": 500,
  "backfill_safety_multiplier": 3,
  "deployed_at": "2026-03-21T14:00:00Z",
  "contract_eligible_after": "2026-03-23T14:00:00Z"
}
```

`backfill_safety_multiplier` defaults to `3`. The contract migration's safety check is `@@ROWCOUNT > backfill_goal * backfill_safety_multiplier`. A value of 3 allows for a reasonable burst of write activity between gate check and contract execution while still catching a broken dual-write (which would produce an order-of-magnitude larger row count). Increase for high-write-volume tables; decrease to tighten the check.

`backfill_goal: 0` is valid for small tables where the inline `suppressTransaction` backfill migration guarantees processing all existing rows before the app deploys. Since new rows are always dual-written, `remaining` reaches and stays at 0 after the backfill migration runs, making the gate achievable.

**Table size classification (read from `/health/detail` on production before migration PR merges):**
- `rows < 10,000` → inline backfill in Phase 2 migration (single UPDATE with `suppressTransaction: true`, isolated migration)
- `rows ≥ 10,000` → `BackfillHostedService` (batched, progress tracked in `__BackfillProgress` table)

#### Phase 2 — Backfill

**For small tables — isolated migration with `suppressTransaction: true`:**
```csharp
// Isolated migration — nothing else in this file
migrationBuilder.Sql(@"
    WHILE (1=1)
    BEGIN
        UPDATE TOP(1000) Todos SET Summary = Title WHERE Summary IS NULL;
        IF @@ROWCOUNT = 0 BREAK;
    END
", suppressTransaction: true);
```

Each iteration of the loop auto-commits (SQL Server auto-commit mode when no explicit transaction is open). If the loop fails partway through, already-committed rows remain updated. On retry, `WHERE Summary IS NULL` is idempotent — already-updated rows are skipped.

**For large tables — `BackfillHostedService`:**
```csharp
public interface IBackfillJob
{
    string MigrationName { get; }
    int BatchSize { get; }
    Task<int> ExecuteBatchAsync(CancellationToken ct); // returns rows processed
    Task<bool> IsCompleteAsync(CancellationToken ct);
}
```

`ExecuteBatchAsync` uses `WHERE NewColumn IS NULL` with an index on the new column to find the next batch — not `OFFSET`. `OFFSET` performs an O(N) scan of all preceding rows on each call, which becomes progressively slower on large tables. `WHERE NewColumn IS NULL` with an index is O(remaining) on every call and scales correctly.

Progress stored in `__BackfillProgress` table — survives app restarts. Exposed via `/health/detail` and the dedicated `/health/migrations` endpoint:
```json
"backfills": [{
  "migration": "Add_Summary_Column",
  "processed": 45000,
  "total": 62000,
  "pct": 72,
  "estimated_completion": "2026-03-21T16:30:00Z"
}]
```

**Application code during Phase 2:**
- Continues dual-writing `Title` and `Summary`
- Continues reading from `Title`
- Partial backfill state is invisible to users — `Title` is always current

#### Phase 3 — Contract

**Contract gate (enforced by CI before the contract PR can merge):**

```bash
# CI queries production app before allowing contract PR merge
# Select by migration name — not array index — to handle multiple in-flight migrations
MIGRATION="Add_Summary_Column"
UNMIGRATED=$(curl -s https://api.prod/health/migrations \
  | jq --arg m "$MIGRATION" '.backfills[] | select(.migration == $m) | .remaining')
GOAL=$(jq --arg m "$MIGRATION" '.[] | select(.migration == $m) | .backfill_goal' migrations/lifecycle.json)

if [ "$UNMIGRATED" -gt "$GOAL" ]; then
  echo "Contract blocked: $UNMIGRATED unmigrated rows exceeds goal of $GOAL"
  exit 1
fi
```

The gate uses `backfill_goal` (not zero) because new rows are always dual-written and never join the unmigrated pool. The pool only shrinks. A threshold of zero is not necessary.

**Static analysis gate (enforced by CI on every PR):**

If `lifecycle.json` shows a pending expand/contract, CI greps the codebase to verify the app still dual-writes both columns. If dual-write code is removed before the contract phase completes, the PR is blocked with an actionable error message.

**Contract migration (fully transactional):**

`@safetyThreshold` is computed from `lifecycle.json` at migration generation time (`backfill_goal * backfill_safety_multiplier`) and embedded as a literal integer in the generated SQL — not a runtime variable. The migration generator reads `lifecycle.json` to produce this value before emitting the migration file.

```csharp
// Step 1: Final catch-up UPDATE (bounded by backfill_goal — fast)
// @safetyThreshold is a compile-time literal, e.g. 3000 = backfill_goal(1000) * multiplier(3)
migrationBuilder.Sql(@"
    DECLARE @count INT;
    UPDATE Todos SET Summary = Title WHERE Summary IS NULL;
    SET @count = @@ROWCOUNT;
    IF @count > 3000
        RAISERROR('Contract safety check failed: %d rows updated, expected ≤ 3000', 16, 1, @count);
", suppressTransaction: false); // runs inside the migration transaction

// Step 2: Add NOT NULL constraint (safe — all rows now populated)
migrationBuilder.AlterColumn<string>("Summary", "Todos", nullable: false);

// Step 3: Drop old column (idempotent via convention)
migrationBuilder.DropColumn("Title", "Todos");
```

All three steps are in one migration, wrapped in EF's transaction. If any step fails, all three roll back. The `__EFMigrationsHistory` record is not written. On retry, the idempotent DDL convention handles any step that already ran.

**Safety multiplier check:** If the final UPDATE finds significantly more rows than `backfill_goal`, it raises an error, the transaction rolls back, a GitHub Issue is opened, and the recovery agent is notified. This catches silent dual-write bugs.

**Application code after contract:**
- Reads and writes `Summary` only — `Title` no longer exists
- Dual-write code removed

**`lifecycle.json` after contract:**
```json
{ "migration": "Add_Summary_Column", "phase": "complete" }
```

### Contract overdue alerts

A weekly scheduled GitHub Actions workflow checks `lifecycle.json` for any expand migration past its `contract_eligible_after` date with no corresponding contract. It creates one GitHub Issue per overdue entry (idempotent — updates existing issue rather than creating duplicates). The issue URL is written back to `lifecycle.json` to prevent duplication:

```json
{ "migration": "Add_Summary_Column", "phase": "expand", "overdue_issue_url": "https://github.com/..." }
```

### Failure scenarios

The following scenarios validate that the design is correct end-to-end. Each traces all relevant design elements.

---

#### Happy Path 1 — New column with runtime constant default

**Setup:** Add `Priority INT NOT NULL DEFAULT 0` to `Todos`.

**Execution:**
1. Single EF migration: `ALTER TABLE Todos ADD Priority INT NOT NULL DEFAULT 0`
2. SQL Server recognises `0` as a runtime constant → metadata-only operation, no table scan, no row locks, completes in milliseconds regardless of table size
3. EF transaction commits. `__EFMigrationsHistory` record written in same transaction.
4. `lifecycle.json`: not modified (single-phase change, no expand/contract needed)
5. App code reads and writes `Priority` immediately
6. No `BackfillHostedService` job registered
7. `/health/detail` shows no pending backfills or contracts

**Idempotent retry test:** If step 3 fails and EF retries, the idempotent convention generates `IF NOT EXISTS (...) ALTER TABLE Todos ADD Priority ...`. Column already exists → statement is a no-op. History INSERT runs. ✅

---

#### Happy Path 2 — Column rename, small table (800 rows)

**Setup:** Rename `Title` → `Summary`. Table has 800 rows. `backfill_goal: 0` (table is small enough that zero unmigrated rows is achievable).

**Phase 1 — Expand:**
1. CI reads `/health/detail`: `Todos.rows = 800` → classifies as small table
2. Migration: `IF NOT EXISTS (...) ALTER TABLE Todos ADD Summary NVARCHAR(200) NULL`
3. EF transaction commits. History written.
4. `lifecycle.json` written: `{ phase: "expand", backfill_goal: 0, old_column: "Title", new_column: "Summary" }`
5. App deploys with dual-write code. All new writes go to both `Title` and `Summary`. Reads from `Title`.
6. `/health/detail`: `{ migrations: { backfills: [{ migration: "...", pct: 0 }] } }`

**Phase 2 — Backfill (inline, small table):**
1. Isolated migration with `suppressTransaction: true`
2. WHILE loop: `UPDATE TOP(1000) Todos SET Summary = Title WHERE Summary IS NULL`
3. 800 rows — completes in one batch, auto-commits
4. History INSERT runs (also suppressed, runs in auto-commit mode)
5. `/health/detail`: `{ backfills: [{ pct: 100, remaining: 0 }] }`

**Contract gate:**
1. CI reads `/health/detail`: `remaining = 0 ≤ backfill_goal = 0` ✅
2. Static analysis: confirms dual-write code present in codebase ✅
3. Contract PR allowed to merge

**Phase 3 — Contract:**
1. Final UPDATE: `UPDATE Todos SET Summary = Title WHERE Summary IS NULL` → 0 rows (all backfilled + all new rows dual-written)
2. `@@ROWCOUNT = 0 ≤ safety_threshold` ✅ no abort
3. `ALTER COLUMN Summary NOT NULL` (idempotent: checks current nullability)
4. `DROP COLUMN Title` (idempotent: `IF EXISTS (...)`)
5. Transaction commits. History written.
6. `lifecycle.json`: `{ phase: "complete" }`
7. App code updated: reads/writes `Summary` only, dual-write code removed

**Result:** ✅ Three phases, no table locks, no user impact throughout.

---

#### Happy Path 3 — Column rename, large table (2M rows)

**Setup:** Rename `Title` → `Summary`. Table has 2,000,000 rows. `backfill_goal: 1000`.

**Phase 1 — Expand:** identical to Happy Path 2 Phase 1. Column added instantly (metadata-only).

**Phase 2 — Backfill (BackfillHostedService):**
1. `TitleToSummaryBackfillJob : IBackfillJob` registered in DI
2. On app startup, `BackfillHostedService` checks `__BackfillProgress` table — no record found → starts from the beginning
3. Processes 1,000 rows per batch with a short delay between batches to avoid lock escalation
4. After each committed batch, updates `__BackfillProgress`: `{ migration: "...", processed: N, total: 2000000 }`
5. `/health/detail` reports progress. New rows written by live app are dual-written — never added to the unmigrated pool
6. After ~2,000 batches, `remaining ≈ 843` (some rows added after backfill started but before they were reached — already dual-written so `Summary` is populated; the remaining 843 are old rows not yet processed)
7. `/health/detail`: `{ backfills: [{ pct: 99.96, remaining: 843 }] }`

**Contract gate:**
1. CI reads `/health/detail`: `remaining = 843 ≤ backfill_goal = 1000` ✅
2. Static analysis confirms dual-write still present ✅
3. Contract PR allowed to merge

**Phase 3 — Contract:**
1. Final UPDATE: `UPDATE Todos SET Summary = Title WHERE Summary IS NULL` → ≤ 1000 rows (fast, bounded)
2. `@@ROWCOUNT = 843 ≤ safety_threshold = 3000` ✅
3. `ALTER COLUMN Summary NOT NULL`
4. `DROP COLUMN Title`
5. Transaction commits. History written.

**Result:** ✅ 2M row table processed without locking, contract fast and bounded.

---

#### Failure 1 — Phase 1 DDL fails, history INSERT never written

**Setup:** Phase 1 migration runs. `ALTER TABLE ADD Summary` succeeds. Due to a transient network issue, EF's transaction cannot commit (timeout). Transaction rolls back — both the DDL and the `__EFMigrationsHistory` INSERT are rolled back together. Column does not exist.

**What happens:**
1. EF catches the exception. Migration marked as failed.
2. `lifecycle.json` has NOT been written yet (CI writes it only after successful migration)
3. App does not deploy (CI pipeline halts on migration failure)
4. GitHub Issue opened with failure context

**Retry:**
1. Idempotent convention: `IF NOT EXISTS (...) ALTER TABLE Todos ADD Summary ...`
2. Column does not exist (DDL was rolled back) → column is added ✅
3. Transaction commits. History written. ✅
4. CI writes `lifecycle.json`: `{ phase: "expand" }` ✅

**Without idempotent convention:** The same retry would also succeed here because the column genuinely doesn't exist (the transaction rolled back). However, the convention is essential for cases where DDL commits but the history INSERT fails (see Failure 2).

**Result:** ✅ Clean retry. Idempotent convention provides safety regardless.

---

#### Failure 2 — DDL commits, history INSERT fails (suppressTransaction edge case)

**Setup:** Phase 2 backfill migration uses `suppressTransaction: true`. The WHILE loop completes successfully. The `__EFMigrationsHistory` INSERT (also suppressed, per EF source code: `transactionSuppressed: operations.Any(o => o.TransactionSuppressed)`) fails due to a transient DB error. EF records the migration as failed.

**Database state after failure:**
- `Summary` column: fully populated for all existing rows ✅
- `__EFMigrationsHistory`: Phase 2 migration NOT recorded ❌
- App: still running, still dual-writing, still reading `Title` ✅
- Users: zero impact — `Title` always has current data ✅

**Retry:**
1. EF sees Phase 2 not in history → runs the migration again
2. WHILE loop: `UPDATE TOP(1000) Todos SET Summary = Title WHERE Summary IS NULL` → 0 rows updated (all already done)
3. Loop exits immediately (idempotent via `WHERE Summary IS NULL`)
4. History INSERT: runs successfully this time ✅
5. `lifecycle.json`: unchanged — still `{ phase: "expand" }`. Phase only advances to `"complete"` when the contract migration runs. The backfill pct visible in `/health/migrations` now shows `pct: 100, remaining: 0`. ✅

**Result:** ✅ `WHERE` clause idempotency makes the retry trivially safe.

---

#### Failure 3 — BackfillHostedService crashes mid-run

**Setup:** Large table (2M rows). BackfillHostedService has processed 500,000 rows (batch 500 of 2000). App pod crashes unexpectedly (OOM, node eviction).

**Database state after crash:**
- `Summary` populated for rows 1–500,000 ✅ (each batch committed independently)
- `Summary` NULL for rows 500,001–2,000,000 ✅ (not yet processed — irrelevant, app reads `Title`)
- `__BackfillProgress` table: `{ processed: 500000, last_batch: 500 }` ✅
- App: restarting (Container Apps restarts automatically)
- Users: zero impact — `Title` always has current data ✅

**On restart:**
1. `BackfillHostedService` starts, reads `__BackfillProgress`
2. Finds `processed: 500000` → resumes using `WHERE Summary IS NULL` (not OFFSET — see batch strategy above)
3. No rows are double-processed — rows already backfilled have `Summary IS NOT NULL` and are excluded by the WHERE clause ✅
4. Progress continues from 500,000 to 2,000,000
5. `lifecycle.json`: unchanged — still `{ phase: "expand", backfill_goal: 1000 }`. `/health/migrations` shows updated `pct` as backfill progresses. ✅

**Result:** ✅ Checkpoint-based resume. No data loss. No user impact.

---

#### Failure 4 — New rows surge between gate check and contract execution

**Setup:** `backfill_goal: 1000`. Gate check passes: `remaining = 743`. High write traffic between gate check and contract migration execution.

**Key question:** Can new rows increase the unmigrated count above `backfill_goal`?

**Answer: No.** New rows are always dual-written by the live application — `Summary` is populated at write time. New rows never enter the unmigrated pool (`WHERE Summary IS NULL`). The unmigrated pool contains only old rows not yet touched by the backfill. The `BackfillHostedService` is still running during this window and the pool only shrinks.

**At contract time:**
1. Final UPDATE finds ≤ 743 rows (pool has only shrunk since the gate check)
2. `@@ROWCOUNT ≤ backfill_goal` ✅
3. `@@ROWCOUNT ≤ backfill_goal * backfill_safety_multiplier (3000)` ✅ safety check passes
4. Contract completes. Transaction commits.
5. `lifecycle.json`: updated to `{ phase: "complete" }` ✅
6. `/health/migrations`: `pending_contracts: []`, `backfills: []` ✅

**Result:** ✅ Dual-write guarantee makes the pool monotonically decreasing.

---

#### Failure 5 — Contract migration fails mid-transaction

**Setup:** Contract migration runs:
1. Final UPDATE: `UPDATE Todos SET Summary = Title WHERE Summary IS NULL` — 743 rows updated, locks held
2. `ALTER COLUMN Summary NOT NULL` — completes
3. `DROP COLUMN Title` — **fails** (e.g., a foreign key constraint on `Title` was missed)
4. EF transaction rolls back

**Database state after rollback:**
- All 743 rows: `Summary` back to their pre-UPDATE state (transaction rolled back) — some may be NULL again
- `Title` column: still exists ✅
- `Summary` column: still nullable (ALTER rolled back) ✅
- `__EFMigrationsHistory`: Phase 3 migration NOT recorded
- App: still dual-writing both columns, still reading `Title` ✅
- Users: zero impact — `Title` always has current data ✅

**`lifecycle.json` state throughout:** unchanged — still `{ phase: "expand" }`. Phase only advances to `"complete"` when the contract transaction commits and history is written. `/health/migrations` still shows the pending contract.

**Retry (after fixing the FK constraint):**
1. Idempotent final UPDATE: 743 rows (same rows as before, `WHERE Summary IS NULL` since they were rolled back) ✅
2. Idempotent `ALTER COLUMN NOT NULL`: checks current nullability, applies if nullable ✅
3. Idempotent `DROP COLUMN`: `IF EXISTS (...) DROP COLUMN Title` ✅
4. Transaction commits. History written. ✅
5. `lifecycle.json`: updated to `{ phase: "complete" }` ✅

**Result:** ✅ Fully transactional contract means clean rollback. Idempotent DDL makes retry safe.

---

#### Failure 6 — Final UPDATE finds far more rows than backfill_goal (dual-write bug)

**Setup:** `backfill_goal: 1000`. Gate passes: `remaining = 843`. A bug in a new code path introduced in the same release skips writing `Summary` for a specific todo creation flow. 4,500 new rows are created without `Summary` during the gate→deploy window.

**At contract time:**
1. Final UPDATE: `UPDATE Todos SET Summary = Title WHERE Summary IS NULL` — finds 843 + 4,500 = 5,343 rows
2. Safety check: `IF @@ROWCOUNT > backfill_goal * 3` → `5343 > 3000` → `RAISERROR`
3. Transaction rolls back ✅
4. `__EFMigrationsHistory`: NOT written
5. App: still dual-writing (what it can), reading `Title` ✅

**`lifecycle.json` state:** unchanged — still `{ phase: "expand" }`. The RAISERROR causes the transaction to roll back; the history INSERT never runs. The contract has not advanced. `/health/migrations` still shows the pending contract with `remaining: 5343`.

**Automatic response:**
1. Pipeline Stage 4 (migration step) fails
2. GitHub Issue opened: "Contract safety check failed — 5,343 unmigrated rows found, goal was 1,000 (multiplier: 3x = 3,000). Likely dual-write bug. Investigate write paths for Summary column."
3. Recovery agent notified out-of-band via SendGrid email
4. Recovery agent analyses the traces for the failure window, identifies the code path that skipped dual-write, drafts a fix PR

**Result:** ✅ Safety multiplier catches silent dual-write failures before data is irrecoverably lost.

---

#### Failure 7 — Idempotent DDL: multi-operation migration, partial commit

**Setup:** A migration contains three operations:
1. `ADD COLUMN SummaryHash BINARY(20) NULL`
2. `ADD COLUMN SummaryVersion INT NOT NULL DEFAULT 1`
3. `CREATE INDEX IX_Todos_SummaryHash ON Todos(SummaryHash)`

EF wraps all three in one transaction (EF9). Step 3 fails (e.g., index name already exists from a previous manual intervention). Transaction rolls back — all three steps are undone.

**Without idempotent convention:**
- Retry: Step 1 → column doesn't exist → adds ✅. Step 2 → column doesn't exist → adds ✅. Step 3 → index already exists from the manual intervention → **fails** ❌

**With idempotent convention:**
- Retry: Step 1 → `IF NOT EXISTS` → column doesn't exist → adds ✅. Step 2 → `IF NOT EXISTS` → column doesn't exist → adds ✅. Step 3 → `IF NOT EXISTS` → index already exists → **skips** ✅. History INSERT runs ✅.

**Result:** ✅ Idempotent convention protects against unexpected pre-existing state from manual interventions.

---

#### Failure 8 — Two expand/contract cycles in flight simultaneously

**Setup:**
```json
lifecycle.json:
[
  { "migration": "Add_Summary", "phase": "expand", "old_column": "Title", "new_column": "Summary", "backfill_goal": 1000 },
  { "migration": "Add_Priority", "phase": "expand", "old_column": null, "new_column": "Priority", "backfill_goal": 0 }
]
```

Note: both entries use `"phase": "expand"`. The phase stays `"expand"` throughout the backfill period — `/health/migrations` shows `pct` progress within that phase. Phase only changes to `"complete"` when the contract migration commits.

App: dual-writes `Title`+`Summary` AND writes `Priority` (no old column for Priority — it's a new column being added, but a future contract will remove an old `Urgency` column). Reads from `Title` and `Urgency`.

**CI gate for `Add_Summary` contract:**
- Checks `remaining` for `Add_Summary` only → 843 ≤ 1000 ✅
- Does NOT block on `Add_Priority` being complete (independent cycles)
- Static analysis: verifies dual-write for `Title`/`Summary` still present ✅
- Static analysis: verifies dual-write for `Urgency`/`Priority` still present ✅ (Add_Priority still in expand phase)

**CI gate for `Add_Priority` contract:**
- Checks `remaining` for `Add_Priority` only → 2,100,000 >> 0 ❌ blocked (BackfillHostedService still running)

**Result:** ✅ Migrations tracked independently. Each gate is scoped to its own migration. No cross-migration dependencies.

---

#### Failure 9 — Engineer removes dual-write code before contract phase

**Setup:** `Add_Summary` is in expand phase. A PR removes the dual-write code (only writes to `Summary`, stops writing to `Title`).

**CI static analysis step:**
1. Reads `lifecycle.json` → finds `{ phase: "expand", old_column: "Title", new_column: "Summary" }`
2. Greps codebase for writes to `Title` → finds none
3. Fails: "Dual-write removed before contract phase for `Add_Summary` migration. `Title` must still be written until the contract migration drops it. See `lifecycle.json` entry for this migration."

**PR blocked.** Developer must either:
- Restore the dual-write code, or
- First complete and merge the contract migration, then remove the dual-write code

**Result:** ✅ Most dangerous human error caught before it can reach production.

---

#### Failure 10 — `suppressTransaction: true` DDL operation, history INSERT fails

**Setup:** A migration adds a memory-optimised table (a valid use of `suppressTransaction: true`). The DDL succeeds. The `__EFMigrationsHistory` INSERT (also suppressed per EF source) fails due to a transient error.

**Database state:**
- Memory-optimised table: **exists** ✅ (DDL auto-committed)
- `__EFMigrationsHistory`: NOT written ❌

**EF9 recommendation applied:** This operation is isolated in its own migration with no other operations.

**Retry:**
1. EF sees migration not in history → runs it again
2. DDL: `IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SessionCache' ...) CREATE TABLE ...` → table exists → statement is a no-op ✅
3. History INSERT: succeeds ✅

**Without idempotent convention:** Retry: `CREATE TABLE SessionCache` → table already exists → **fails** ❌. This would require manual intervention to insert the history record directly.

**Result:** ✅ Idempotent convention is essential for `suppressTransaction` operations. This is the primary scenario it was designed for.

---

#### Failure 11 — Overdue contract, nobody ships it

**Setup:** `Add_Summary` expand migration deployed 2026-01-15. `contract_eligible_after: 2026-01-17`. It is now 2026-03-21. No contract PR has been raised.

**Routine CI runs:** Do NOT alert on every run. The overdue state is noted but not acted on per-commit.

**Weekly scheduled GitHub Actions job:**
1. Reads `lifecycle.json`
2. Finds `Add_Summary` with `contract_eligible_after: 2026-01-17` and no `phase: "complete"`
3. Checks for existing GitHub Issue (reads `overdue_issue_url` field) → not present
4. Creates GitHub Issue: "[Contract Overdue] Remove `Title` column — eligible since 2026-01-17"
5. Writes issue URL back to `lifecycle.json`: `{ ..., "overdue_issue_url": "https://github.com/..." }`
6. Commits `lifecycle.json` update
7. Next weekly run: finds `overdue_issue_url` → updates existing issue instead of creating a new one

**You receive:** A GitHub notification (normal GitHub workflow — no noise on every commit). The issue contains the migration name, the eligible date, and the current `/health/detail` output showing backfill progress.

**Result:** ✅ Technical debt surfaces on a manageable cadence. No per-commit noise.

---

## 13. CI/CD Pipeline

### Trigger

All branches on push. Deployment stages only run on the main branch.

### Stages

```
┌─────────────────────────────────────────────────────────────────┐
│ Stage 1 — Unit Tests                                            │
│   dotnet test --filter Category=Unit                            │
│   Duration: < 2 seconds                                         │
│   Output: JUnit XML artifact, GitHub step summary               │
│   Fails fast. All subsequent stages blocked if this fails.      │
├─────────────────────────────────────────────────────────────────┤
│ Stage 2 — Integration Tests                                     │
│   dotnet test --filter Category=Integration                     │
│   Requires: Docker (Testcontainers SQL Server + SB emulator)    │
│   Output: JUnit XML artifact, code coverage report              │
│   Runs only if Stage 1 passes.                                  │
├─────────────────────────────────────────────────────────────────┤
│ Stage 3 — Build & Publish                                       │
│   dotnet publish → Docker images for Api, Web, Mcp.Tools,       │
│                    Mcp.Composite                                 │
│   Pushes images to Azure Container Registry                     │
│   Output: build metadata JSON { sha, version, images[], time }  │
├─────────────────────────────────────────────────────────────────┤
│ Stage 4 — Database Migrations          [main branch only]       │
│   dotnet ef migrations script --idempotent --output migrate.sql │
│   sqlcmd -S $SERVER -d $DB -i migrate.sql                       │
│   Generates idempotent SQL script and applies via sqlcmd.       │
│   Never uses dotnet ef database update / MigrateAsync() in      │
│   production — per Microsoft guidance (applying-migrations).    │
│   Runs against production DB while current app serves traffic.  │
│   Only DDL — fast, transactional, safe.                         │
│   Updates lifecycle.json if new expand/contract cycle begins.   │
│   Output: migration result JSON { applied[], duration_ms }      │
├─────────────────────────────────────────────────────────────────┤
│ Stage 5 — Deploy to Green Revision     [main branch only]       │
│   Octopus Deploy: pushes new images to Container Apps           │
│   New revision receives 0% traffic                              │
│   Accessible only via label URL for verification                │
│   Output: revision name, label URL                              │
├─────────────────────────────────────────────────────────────────┤
│ Stage 6 — Verify Green Revision        [main branch only]       │
│   GET {label-url}/health/detail                                  │
│   → assert version matches build SHA                            │
│   → assert all deps healthy (db, bus, email)                    │
│   GET {label-url}/health/migrations                             │
│   → assert no failed migrations                                 │
│   Smoke tests via Kiota CLI against label URL                   │
│   Output: verification result JSON                              │
├─────────────────────────────────────────────────────────────────┤
│ Stage 7 — Cut Over                     [main branch only]       │
│   az containerapp ingress traffic set --label-weight blue=0 green=100  │
│   Output: cutover timestamp, revision names                     │
├─────────────────────────────────────────────────────────────────┤
│ Stage 8 — Post-Deploy Verification     [main branch only]       │
│   GET /health/detail → assert version, all deps healthy         │
│   Query Azure Monitor → assert error rate within bounds         │
│   Capture business metrics baseline for this version            │
│   Duration: 2 minutes of monitoring                             │
│   Output: verification JSON artifact                            │
└─────────────────────────────────────────────────────────────────┘
```

### On any stage failure (Stages 4–8)

1. Pipeline halts
2. GitHub Issue opened automatically with: failed stage, structured error output, traces for the failure window, current `/health/detail` output
3. Recovery agent triggered (see Section 15)
4. Out-of-band alert sent via SendGrid email
5. **No automatic rollback.** If a migration ran, rollback is not safe. Team moves forward.

### Agent-readable pipeline output

Every stage emits structured JSON via GitHub Actions step outputs:

```bash
gh run view {run-id} --json steps,conclusion,status
gh run view {run-id} --log-failed
```

Agents use this to understand exactly which stage failed and why, without parsing human-readable logs.

---

## 14. Deployment — Azure Container Apps

### Configuration

- **Plan:** Consumption (no dedicated plan)
- **Default:** `minReplicas: 0` (scale-to-zero)
- **Upgrade path:** set `minReplicas: 1` when business metrics indicate sufficient regular usage
- **Scale-to-zero behaviour:** first request after idle (5-minute cool-down) takes 10–30 seconds while the container starts. The request is buffered by the platform — users see a slow response, not an error.
- **Cold start mitigation:** optional scheduled Container Apps job to send a warm-up request at known active hours (e.g., 08:00 UTC)

**References:**
- Scale-to-zero cold start: 10–30 seconds, request buffered. Source: [Azure Container Apps cold start](https://learn.microsoft.com/azure/container-apps/cold-start)
- Cool-down period before scale-to-zero: 300 seconds. Source: [Scale rules](https://learn.microsoft.com/azure/container-apps/scale-app#scale-behavior)
- Free grant: 180,000 vCPU-seconds + 360,000 GiB-seconds + 2M requests per subscription per month. At 100 req/day this app uses < 1% of the free grant. Source: [Container Apps billing](https://learn.microsoft.com/azure/container-apps/billing)

### Blue-green deployment via revisions

Each deployment creates a new revision with 0% production traffic. Traffic shifts to the new revision only after Stage 6 verification passes.

```bash
# Stage 5: deploy green, 0% traffic
az containerapp update --image api:v2 --revision-suffix v2
az containerapp revision label add --label green --revision app--v2
az containerapp ingress traffic set --label-weight blue=100 green=0

# Stage 6: verify via label URL (not production traffic)
curl https://app---green.{env-domain}/health/detail

# Stage 7: cut over
az containerapp ingress traffic set --label-weight blue=0 green=100

# On failure: instant rollback
az containerapp ingress traffic set --label-weight blue=100 green=0
az containerapp revision deactivate --revision app--v2
```

### Octopus Deploy integration

Octopus Deploy Cloud (SaaS) orchestrates Container Apps deployments via the Azure CLI community step template and/or "Run a Script" steps with the Azure CLI. No native Container Apps step exists in Octopus; the az CLI commands above are parameterised as Octopus variables.

---

## 15. Backup and Recovery

### Azure SQL automated backups

Azure SQL Database provides automatic point-in-time restore (PITR) with configurable retention. No additional tooling required.

**Pipeline integration:**

- **Pre-deploy gate (Stage 4):** CI verifies the latest backup timestamp is within 24 hours before running migrations
- **Post-deploy backup point:** after Stage 8 verification, CI creates a named restore point tagged with version and build SHA: `post-deploy-v1.0.42-2026-03-21T14:22:00Z`

### Autonomous recovery agent

When a pipeline failure (Stages 4–8) or post-deploy metric degradation is detected, a GitHub Actions workflow triggers a headless Claude agent via the Anthropic API. No chat window required.

**Agent inputs:**
- Failed stage and structured error output
- Current `/health/detail` output
- Azure Monitor traces for the failure window
- Current `lifecycle.json` state
- Git diff of what was deployed
- Most recent backup timestamp and restore point names

**Agent outputs (posted to GitHub Issue):**
1. Root cause analysis (which stage, which component, what the traces show)
2. Recovery type classification: app bug / migration issue / infrastructure / data corruption
3. Draft recovery PR with suggested fix
4. If a migration was involved: specific SQL to audit data state
5. If data corruption suspected: `az sql db restore` command with exact timestamp and resource names
6. Recommended next steps (ordered, explicit)

**What requires your approval before execution:**
- Merging the recovery PR
- Running any SQL against production
- Triggering a database restore
- Redeploying a previous version

The agent surfaces these as options with complete commands. You approve each step.

**Out-of-band alert:** SendGrid email sent immediately on failure with a link to the GitHub Issue. Arrives regardless of whether a chat window is open.

### Data restore flow (if required)

```
1. Enable maintenance mode (Container Apps: route all traffic to a static maintenance revision)
   az containerapp ingress traffic set \
     --name app-todolist \
     --resource-group rg-todolist \
     --label-weight production=0 maintenance=100

2. Restore DB to last known good point
   az sql db restore \
     --resource-group rg-todolist \
     --server sql-todolist \
     --name db-todolist-restored \
     --time "{restore-point-timestamp}"

3. Rename restored DB into production slot

4. Redeploy last known good app version via Octopus

5. Verify
   GET /health/detail → assert version and db:ok

6. Disable maintenance mode
```

All commands are generated by the recovery agent with real resource names and timestamps. Each step requires explicit approval.

---

## 16. Infrastructure as Code

### Split ownership

| Tool | Owns | Rationale |
|---|---|---|
| Bicep (via Aspire + azd) | Azure SQL, Azure Service Bus, App Insights, Container Apps environment, Container Apps definitions | Aspire AppHost auto-generates Bicep from resource declarations |
| Pulumi C# | Alert rules, backup retention policies, Key Vault, recovery agent infrastructure, GitHub Action secrets (via API) | Not generated by Aspire; benefits from C# abstractions and xUnit tests |

### Integration

`azd` exports all Bicep outputs as environment variables after `azd provision`. Pulumi reads these directly:

```yaml
# azure.yaml
hooks:
  postprovision:
    shell: sh
    run: pulumi up --stack prod --yes
```

```csharp
// Pulumi reads azd outputs via env vars
var sqlServerId = Environment.GetEnvironmentVariable("AZURE_SQL_SERVER_ID");
var sqlServer = SqlServer.Get("sql", sqlServerId);
```

### Full IaC design

A dedicated IaC brainstorm and spec will cover:
- Bicep module structure (generated by Aspire vs hand-authored)
- Pulumi program structure and stack layout
- Environment promotion (dev → staging → production)
- Secret management (Key Vault, GitHub Actions secrets)
- Networking and access policies

---

## 17. Agent Workflow

### During development (TDD loop)

```bash
# Fast feedback after every change — no infrastructure required
dotnet test --filter Category=Unit          # < 2 seconds

# Verify API behaviour locally
todocli todos add --title "test item"
todocli todos list
GET http://localhost:5000/health/detail

# Before pushing — full integration test suite
dotnet test --filter Category=Integration   # requires Docker
```

### After push — pipeline monitoring

```bash
# Watch pipeline live
gh run watch

# Read structured pass/fail per stage
gh run view {run-id} --json steps,conclusion,status

# On failure: read structured failure output
gh run view {run-id} --log-failed
```

### After deployment

```bash
# Verify correct version landed and all deps healthy
curl https://api.prod/health/detail
# assert: version == build SHA, db == "ok", bus == "ok"

# Check migration state
curl https://api.prod/health/migrations
# assert: no failed backfills, no unexpected pending contracts

# Check business metrics vs previous version
az monitor metrics list \
  --resource {app-insights-id} \
  --metric "todos.completion_rate" \
  --filter "version eq 'abc123'"
```

### Mcp.Composite interface for direct agent operation

```bash
# Discover capabilities for a stated intent (Mcp.Composite)
POST /plan { "about": "how do I create and complete a todo?" }

# Execute a multi-step plan in one call (Mcp.Composite)
POST /execute {
  "operations": [
    { "op": "create_todo", "params": { "title": "verify deployment" } },
    { "op": "complete_todo", "params": { "id": "$result[0].id" } }
  ]
}

# Verify business state after deployment
POST /plan { "about": "what are the current completion metrics?" }
```

### Recovery scenario

```bash
# Pipeline failure detected
gh run view {run-id} --json  # → structured failure context

# Recovery agent has already:
# 1. Opened a GitHub Issue with root cause analysis
# 2. Sent an email alert
# 3. Created a draft recovery PR

# You review the GitHub Issue, approve the recovery PR, and merge
# Agent monitors the follow-up deployment and verifies resolution
```
