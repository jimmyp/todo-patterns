# Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the existing single-project todo app into the 8-project reference architecture with a rich domain model, EF Core persistence, async REST API, and full unit + integration test coverage.

**Architecture:** Domain logic lives entirely in `TodoList.Api/Domain/` — aggregates, events, and `DomainResult<T>`. The API layer is thin Minimal API endpoints that call domain methods and persist results. Integration tests spin up a real SQL Server container via Testcontainers.

**Tech Stack:** .NET 8, ASP.NET Core Minimal API, Entity Framework Core 8 (SQL Server), .NET Aspire, xUnit, FluentAssertions, Testcontainers.MsSql

---

## File Map

Files created or modified by this plan. Read these before touching any file.

### New Projects

```
TodoList.Api/
  TodoList.Api.csproj
  Program.cs
  Domain/
    DomainResult.cs               # DomainResult<T> — Ok/Fail, never throw
    IDomainEvent.cs               # Marker interface for domain events
    Todo.cs                       # Todo aggregate — Create, Complete, Uncomplete, Delete
    Events/
      TodoCreatedEvent.cs
      TodoCompletedEvent.cs
      TodoUncompletedEvent.cs
      TodoDeletedEvent.cs
  Data/
    TodoDbContext.cs
    DesignTimeDbContextFactory.cs # Required for dotnet ef migrations add
    ITodoRepository.cs
    TodoRepository.cs
    IOperationRepository.cs
    OperationRepository.cs
  Operations/
    TodoOperation.cs
  Endpoints/
    TodoEndpoints.cs              # GET /todos, GET /todos/{id}, POST /todos,
                                  # POST /todos/{id}/complete, /uncomplete, DELETE /todos/{id}
    OperationEndpoints.cs         # GET /todos/operations/{id}
    # Note: /health/live and /health/ready are mapped by ServiceDefaults.MapDefaultEndpoints()

TodoList.Web/
  TodoList.Web.csproj             # Stub — Plan 2 implements this fully
  Program.cs

TodoList.AppHost/
  TodoList.AppHost.csproj
  Program.cs

TodoList.ServiceDefaults/
  TodoList.ServiceDefaults.csproj
  Extensions.cs

TodoList.Mcp.Tools/
  TodoList.Mcp.Tools.csproj       # Stub — Plan 5 implements this
  Program.cs

TodoList.Mcp.Composite/
  TodoList.Mcp.Composite.csproj   # Stub — Plan 6 implements this
  Program.cs

TodoList.IntegrationTests/
  TodoList.IntegrationTests.csproj
  GlobalUsings.cs
  Fixtures/
    ApiFixture.cs
  Api/
    TodoEndpointsTests.cs
    OperationEndpointsTests.cs
    HealthEndpointsTests.cs
```

### Modified Files

```
TodoList.sln                           # Add 7 new projects; remove old TodoList project
TodoList.Tests/TodoList.Tests.csproj   # Replace TodoList ref with TodoList.Api
TodoList.Tests/GlobalUsings.cs         # Update usings
TodoList.Tests/TodoServiceTests.cs     # DELETE — replaced by Domain/TodoTests.cs
TodoList.Tests/Domain/
  TodoTests.cs                         # New — tests all domain aggregate methods
```

---

## Task 1: Prerequisites

Verify required tooling is installed.

- [ ] **Step 1: Check .NET 8 SDK**

```bash
dotnet --version
```
Expected: `8.x.x`

- [ ] **Step 2: Install .NET Aspire workload (if not present)**

```bash
dotnet workload list
```
If `aspire` is not listed:
```bash
dotnet workload install aspire
```

Note: `dotnet new aspire-servicedefaults` scaffolds all required OTel and service discovery packages into `TodoList.ServiceDefaults`. No manual package installation is needed for that project.

- [ ] **Step 3: Verify Docker is running** (required for Testcontainers + Aspire SQL)

```bash
docker info
```
Expected: no error, shows server info.

---

## Task 2: Solution Restructure

Create 7 new projects and wire them into the solution. Do not delete the old `TodoList` folder yet — it becomes `TodoList.Web` in Plan 2.

- [ ] **Step 1: Create new projects**

Run from the solution root (`/Users/jim/code/todo-patterns`):

```bash
dotnet new webapi -n TodoList.Api --no-openapi -o TodoList.Api
dotnet new web -n TodoList.Web -o TodoList.Web
dotnet new aspire-apphost -n TodoList.AppHost -o TodoList.AppHost
dotnet new aspire-servicedefaults -n TodoList.ServiceDefaults -o TodoList.ServiceDefaults
dotnet new web -n TodoList.Mcp.Tools -o TodoList.Mcp.Tools
dotnet new web -n TodoList.Mcp.Composite -o TodoList.Mcp.Composite
dotnet new xunit -n TodoList.IntegrationTests -o TodoList.IntegrationTests
```

- [ ] **Step 2: Add all new projects to the solution**

```bash
dotnet sln add TodoList.Api/TodoList.Api.csproj
dotnet sln add TodoList.Web/TodoList.Web.csproj
dotnet sln add TodoList.AppHost/TodoList.AppHost.csproj
dotnet sln add TodoList.ServiceDefaults/TodoList.ServiceDefaults.csproj
dotnet sln add TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj
dotnet sln add TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj
dotnet sln add TodoList.IntegrationTests/TodoList.IntegrationTests.csproj
```

- [ ] **Step 3: Remove old TodoList project from solution** (leave folder — Web will reuse it in Plan 2)

```bash
dotnet sln remove TodoList/TodoList.csproj
```

- [ ] **Step 4: Add project references**

```bash
# Api and Web reference ServiceDefaults
dotnet add TodoList.Api/TodoList.Api.csproj reference TodoList.ServiceDefaults/TodoList.ServiceDefaults.csproj
dotnet add TodoList.Web/TodoList.Web.csproj reference TodoList.ServiceDefaults/TodoList.ServiceDefaults.csproj

# AppHost references Api and Web (for Aspire orchestration)
dotnet add TodoList.AppHost/TodoList.AppHost.csproj reference TodoList.Api/TodoList.Api.csproj
dotnet add TodoList.AppHost/TodoList.AppHost.csproj reference TodoList.Web/TodoList.Web.csproj

# Tests reference Api (to access domain model)
dotnet add TodoList.Tests/TodoList.Tests.csproj reference TodoList.Api/TodoList.Api.csproj
dotnet add TodoList.IntegrationTests/TodoList.IntegrationTests.csproj reference TodoList.Api/TodoList.Api.csproj
```

- [ ] **Step 5: Add NuGet packages to each project**

```bash
# TodoList.Api — EF Core + Aspire component
dotnet add TodoList.Api/TodoList.Api.csproj package Aspire.Microsoft.EntityFrameworkCore.SqlServer
dotnet add TodoList.Api/TodoList.Api.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add TodoList.Api/TodoList.Api.csproj package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
dotnet add TodoList.Api/TodoList.Api.csproj package Microsoft.AspNetCore.OpenApi

# TodoList.AppHost — Aspire hosting + SQL Server resource
dotnet add TodoList.AppHost/TodoList.AppHost.csproj package Aspire.Hosting.AppHost
dotnet add TodoList.AppHost/TodoList.AppHost.csproj package Aspire.Hosting.SqlServer

# TodoList.Tests — xUnit + FluentAssertions
dotnet add TodoList.Tests/TodoList.Tests.csproj package FluentAssertions

# TodoList.IntegrationTests — Testcontainers + WebApplicationFactory
dotnet add TodoList.IntegrationTests/TodoList.IntegrationTests.csproj package FluentAssertions
dotnet add TodoList.IntegrationTests/TodoList.IntegrationTests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add TodoList.IntegrationTests/TodoList.IntegrationTests.csproj package Microsoft.EntityFrameworkCore.SqlServer
dotnet add TodoList.IntegrationTests/TodoList.IntegrationTests.csproj package Testcontainers.MsSql
```

- [ ] **Step 6: Update `TodoList.Tests.csproj` — fix the project reference**

The old `TodoList.Tests.csproj` references the removed `TodoList` project. Open `TodoList.Tests/TodoList.Tests.csproj` and remove the old reference to `../TodoList/TodoList.csproj` (the `dotnet add` in Step 4 already added the correct one).

- [ ] **Step 7: Update `TodoList.Tests/GlobalUsings.cs`**

Replace the contents:

```csharp
global using FluentAssertions;
global using Xunit;
global using TodoList.Api.Domain;
global using TodoList.Api.Domain.Events;
```

- [ ] **Step 8: Delete the old test file**

```bash
rm TodoList.Tests/TodoServiceTests.cs
```

- [ ] **Step 9: Verify solution builds (errors expected until we write code)**

```bash
dotnet build TodoList.sln
```

Build will fail for `TodoList.Tests` (no test files). That's fine — proceed.

---

## Task 3: ServiceDefaults

The `ServiceDefaults` project is shared infrastructure: OTel registration, health check wiring, resilience policies. All other service projects call `builder.AddServiceDefaults()` on startup.

- [ ] **Step 1: Replace generated `Extensions.cs`** with `TodoList.ServiceDefaults/Extensions.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation();
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        return app;
    }
}
```

---

## Task 4: AppHost

AppHost wires all services together for local development via .NET Aspire. When you run AppHost, it starts SQL Server in Docker, the Api, and the Web stub — and opens the Aspire Dashboard.

- [ ] **Step 1: Replace generated `Program.cs`** in `TodoList.AppHost/`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .AddDatabase("todolist");

var api = builder.AddProject<Projects.TodoList_Api>("api")
    .WithReference(sql)
    .WaitFor(sql);

builder.AddProject<Projects.TodoList_Web>("web")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
```

---

## Task 5: Domain Primitives — DomainResult<T> and IDomainEvent

These are the two foundational types every domain method depends on. Write the tests first, then implement.

- [ ] **Step 1: Write the failing tests** — create `TodoList.Tests/Domain/DomainResultTests.cs`:

```csharp
namespace TodoList.Tests.Domain;

[Trait("Category", "Unit")]
public class DomainResultTests
{
    [Fact]
    public void Ok_IsSuccess_true_and_has_value()
    {
        var result = DomainResult<int>.Ok(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Fail_IsSuccess_false_and_has_errors()
    {
        var result = DomainResult<int>.Fail("error one", "error two");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().BeEquivalentTo(["error one", "error two"]);
        result.Value.Should().Be(default);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v minimal
```
Expected: compilation error — `DomainResult` not found. Good.

- [ ] **Step 3: Create `TodoList.Api/Domain/DomainResult.cs`**

```csharp
namespace TodoList.Api.Domain;

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

- [ ] **Step 4: Create `TodoList.Api/Domain/IDomainEvent.cs`**

```csharp
namespace TodoList.Api.Domain;

public interface IDomainEvent { }
```

- [ ] **Step 5: Create domain event records** — `TodoList.Api/Domain/Events/TodoCreatedEvent.cs`:

```csharp
namespace TodoList.Api.Domain.Events;

public record TodoCreatedEvent(
    Guid TodoId,
    string Title,
    DateTimeOffset CreatedAt) : IDomainEvent;

public record TodoCompletedEvent(
    Guid TodoId,
    DateTimeOffset CompletedAt) : IDomainEvent;

public record TodoUncompletedEvent(
    Guid TodoId) : IDomainEvent;

public record TodoDeletedEvent(
    Guid TodoId,
    DateTimeOffset DeletedAt) : IDomainEvent;
```

- [ ] **Step 6: Run tests and verify they pass**

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v minimal
```
Expected: 2 passing tests.

- [ ] **Step 7: Commit**

```bash
git add TodoList.Api/Domain/ TodoList.Tests/Domain/DomainResultTests.cs
git commit -m "feat: add DomainResult<T>, IDomainEvent, and domain events"
```

---

## Task 6: Todo Aggregate — Create

The `Todo.Create` factory method is the only way to produce a valid `Todo`. Write tests for it, then implement.

- [ ] **Step 1: Write the failing tests** — create `TodoList.Tests/Domain/TodoTests.cs`:

```csharp
namespace TodoList.Tests.Domain;

[Trait("Category", "Unit")]
public class TodoTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_with_valid_title_returns_todo_and_created_event()
    {
        var result = Todo.Create("buy milk", Now);

        result.IsSuccess.Should().BeTrue();
        var (todo, events) = result.Value!;
        todo.Title.Should().Be("buy milk");
        todo.IsCompleted.Should().BeFalse();
        todo.IsDeleted.Should().BeFalse();
        todo.CreatedAt.Should().Be(Now);
        events.Should().ContainSingle().Which.Should().BeOfType<TodoCreatedEvent>();
    }

    [Fact]
    public void Create_trims_title_whitespace()
    {
        var result = Todo.Create("  buy milk  ", Now);
        result.Value!.todo.Title.Should().Be("buy milk");
    }

    [Fact]
    public void Create_with_empty_title_returns_error_without_producing_todo()
    {
        var result = Todo.Create("", Now);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Title cannot be empty");
        result.Value.Should().Be(default);
    }

    [Fact]
    public void Create_with_whitespace_title_returns_error()
    {
        var result = Todo.Create("   ", Now);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Create_with_title_over_500_chars_returns_error()
    {
        var result = Todo.Create(new string('x', 501), Now);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Title cannot exceed 500 characters");
    }

    [Fact]
    public void Create_with_empty_and_too_long_title_returns_both_errors()
    {
        // Edge case: whitespace string longer than 500 chars
        var result = Todo.Create(new string(' ', 501), Now);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v minimal
```
Expected: compilation error — `Todo` not found.

- [ ] **Step 3: Create `TodoList.Api/Domain/Todo.cs`** with just the `Create` method:

```csharp
namespace TodoList.Api.Domain;

public class Todo
{
    private Todo() { }  // EF Core requires parameterless constructor

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public bool IsCompleted { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public static DomainResult<(Todo todo, IReadOnlyList<IDomainEvent> events)> Create(
        string title, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(title)) errors.Add("Title cannot be empty");
        if (title?.Length > 500)             errors.Add("Title cannot exceed 500 characters");
        if (errors.Count > 0)
            return DomainResult<(Todo, IReadOnlyList<IDomainEvent>)>.Fail([..errors]);

        var todo = new Todo
        {
            Id        = Guid.NewGuid(),
            Title     = title!.Trim(),
            CreatedAt = now
        };

        return DomainResult<(Todo, IReadOnlyList<IDomainEvent>)>.Ok(
            (todo, [new Events.TodoCreatedEvent(todo.Id, todo.Title, now)]));
    }
}
```

- [ ] **Step 4: Run tests and verify they pass**

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v minimal
```
Expected: all tests pass (DomainResultTests + TodoTests for Create).

- [ ] **Step 5: Commit**

```bash
git add TodoList.Api/Domain/Todo.cs TodoList.Tests/Domain/TodoTests.cs
git commit -m "feat: add Todo aggregate with Create factory method"
```

---

## Task 7: Todo Aggregate — Complete and Uncomplete

These methods advance state. Tests must go through `Create` first to produce a valid `Todo`.

- [ ] **Step 1: Add Complete and Uncomplete tests** to `TodoTests.cs`:

```csharp
// ── Complete ─────────────────────────────────────────────────────────────

[Fact]
public void Complete_on_open_todo_returns_completed_event()
{
    var todo = Todo.Create("buy milk", Now).Value!.todo;

    var result = todo.Complete(Now);

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().ContainSingle().Which.Should().BeOfType<TodoCompletedEvent>();
    todo.IsCompleted.Should().BeTrue();
    todo.CompletedAt.Should().Be(Now);
}

[Fact]
public void Complete_already_completed_todo_returns_error_without_changing_state()
{
    var todo = Todo.Create("buy milk", Now).Value!.todo;
    todo.Complete(Now);

    var result = todo.Complete(Now);

    result.IsSuccess.Should().BeFalse();
    result.Errors.Should().Contain("Already completed");
    // State did not change again
    todo.IsCompleted.Should().BeTrue();
}

[Fact]
public void Complete_deleted_todo_returns_error()
{
    var todo = Todo.Create("buy milk", Now).Value!.todo;
    todo.Delete(Now);

    var result = todo.Complete(Now);

    result.IsSuccess.Should().BeFalse();
    result.Errors.Should().Contain("Cannot complete a deleted todo");
}

// ── Uncomplete ───────────────────────────────────────────────────────────

[Fact]
public void Uncomplete_after_complete_returns_uncompleted_event()
{
    var todo = Todo.Create("buy milk", Now).Value!.todo;
    todo.Complete(Now);

    var result = todo.Uncomplete();

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().ContainSingle().Which.Should().BeOfType<TodoUncompletedEvent>();
    todo.IsCompleted.Should().BeFalse();
    todo.CompletedAt.Should().BeNull();
}

[Fact]
public void Uncomplete_open_todo_returns_error_without_changing_state()
{
    var todo = Todo.Create("buy milk", Now).Value!.todo;

    var result = todo.Uncomplete();

    result.IsSuccess.Should().BeFalse();
    result.Errors.Should().Contain("Not completed");
}

[Fact]
public void Uncomplete_deleted_todo_returns_error()
{
    var todo = Todo.Create("buy milk", Now).Value!.todo;
    todo.Complete(Now);
    todo.Delete(Now);

    var result = todo.Uncomplete();

    result.IsSuccess.Should().BeFalse();
    result.Errors.Should().Contain("Cannot uncomplete a deleted todo");
}
```

- [ ] **Step 2: Run tests and verify they fail**

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v minimal
```
Expected: compilation errors — `Complete`, `Uncomplete`, `Delete` not found.

- [ ] **Step 3: Add `Complete` and `Uncomplete` to `Todo.cs`**

```csharp
public DomainResult<IReadOnlyList<IDomainEvent>> Complete(DateTimeOffset now)
{
    var errors = new List<string>();
    if (IsDeleted)   errors.Add("Cannot complete a deleted todo");
    if (IsCompleted) errors.Add("Already completed");
    if (errors.Count > 0)
        return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

    IsCompleted = true;
    CompletedAt = now;
    return DomainResult<IReadOnlyList<IDomainEvent>>.Ok(
        [new Events.TodoCompletedEvent(Id, now)]);
}

public DomainResult<IReadOnlyList<IDomainEvent>> Uncomplete()
{
    var errors = new List<string>();
    if (IsDeleted)   errors.Add("Cannot uncomplete a deleted todo");
    if (!IsCompleted) errors.Add("Not completed");
    if (errors.Count > 0)
        return DomainResult<IReadOnlyList<IDomainEvent>>.Fail([..errors]);

    IsCompleted = false;
    CompletedAt = null;
    return DomainResult<IReadOnlyList<IDomainEvent>>.Ok(
        [new Events.TodoUncompletedEvent(Id)]);
}
```

Also add a stub `Delete` to fix the compile error in the Complete test:

```csharp
public DomainResult<IReadOnlyList<IDomainEvent>> Delete(DateTimeOffset now)
{
    throw new NotImplementedException();
}
```

- [ ] **Step 4: Run Complete/Uncomplete tests and verify they pass** (Delete test will be skipped due to NotImplementedException — that's expected)

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v minimal
```
Expected: Complete and Uncomplete tests pass. `Complete_deleted_todo` throws NotImplementedException — fix in next task.

---

## Task 8: Todo Aggregate — Delete

- [ ] **Step 1: Add Delete tests** to `TodoTests.cs`:

```csharp
// ── Delete ───────────────────────────────────────────────────────────────

[Fact]
public void Delete_open_todo_returns_deleted_event()
{
    var todo = Todo.Create("buy milk", Now).Value!.todo;

    var result = todo.Delete(Now);

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().ContainSingle().Which.Should().BeOfType<TodoDeletedEvent>();
    todo.IsDeleted.Should().BeTrue();
    todo.DeletedAt.Should().Be(Now);
}

[Fact]
public void Delete_completed_todo_returns_deleted_event()
{
    var todo = Todo.Create("buy milk", Now).Value!.todo;
    todo.Complete(Now);

    var result = todo.Delete(Now);

    result.IsSuccess.Should().BeTrue();
    todo.IsDeleted.Should().BeTrue();
}

[Fact]
public void Delete_already_deleted_todo_returns_error_without_changing_state()
{
    var todo = Todo.Create("buy milk", Now).Value!.todo;
    todo.Delete(Now);

    var result = todo.Delete(Now);

    result.IsSuccess.Should().BeFalse();
    result.Errors.Should().Contain("Already deleted");
}
```

- [ ] **Step 2: Run tests and verify Delete tests fail**

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v minimal
```

- [ ] **Step 3: Replace `Delete` stub** in `Todo.cs` with real implementation:

```csharp
public DomainResult<IReadOnlyList<IDomainEvent>> Delete(DateTimeOffset now)
{
    if (IsDeleted)
        return DomainResult<IReadOnlyList<IDomainEvent>>.Fail("Already deleted");

    IsDeleted = true;
    DeletedAt = now;
    return DomainResult<IReadOnlyList<IDomainEvent>>.Ok(
        [new Events.TodoDeletedEvent(Id, now)]);
}
```

- [ ] **Step 4: Run all unit tests and verify they all pass**

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v minimal
```
Expected: all tests green. Count: 2 (DomainResult) + 15 (Todo: 6 Create + 3 Complete + 3 Uncomplete + 3 Delete) = 17 passing.

- [ ] **Step 5: Commit**

```bash
git add TodoList.Api/Domain/Todo.cs TodoList.Tests/Domain/TodoTests.cs
git commit -m "feat: add Complete, Uncomplete, Delete to Todo aggregate"
```

---

## Task 9: EF Core — DbContext + Repositories

The repositories are tested indirectly through the full API integration tests (Tasks 11–13). No dedicated repository test task is needed — repository correctness is verified by `PostTodo_todo_appears_in_get_list`, `CompleteTodo_returns_202`, etc.

- [ ] **Step 1: Create `TodoList.Api/Operations/TodoOperation.cs`**

```csharp
namespace TodoList.Api.Operations;

public class TodoOperation
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "pending";  // pending | processing | complete | failed
    public string? ResultJson { get; set; }
    public string? FailureReason { get; set; }
    public bool IsRetryable { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

- [ ] **Step 2: Create `TodoList.Api/Data/TodoDbContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using TodoList.Api.Domain;
using TodoList.Api.Operations;

namespace TodoList.Api.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();
    public DbSet<TodoOperation> Operations => Set<TodoOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Title).HasMaxLength(500).IsRequired();
            b.HasQueryFilter(t => !t.IsDeleted);
        });

        modelBuilder.Entity<TodoOperation>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Status).HasMaxLength(20).IsRequired();
            b.Property(o => o.FailureReason).HasMaxLength(2000);
        });
    }
}
```

- [ ] **Step 3: Create `TodoList.Api/Data/DesignTimeDbContextFactory.cs`**

This is required so that `dotnet ef migrations add` can build a DbContext without a running app:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TodoList.Api.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TodoDbContext>
{
    public TodoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TodoDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=TodoList;User Id=sa;Password=Password123!;" +
                "TrustServerCertificate=True")
            .Options;
        return new TodoDbContext(options);
    }
}
```

Note: This connection string is only used during design-time migration generation. A local SQL Server (from AppHost) must be running for `dotnet ef database update`.

- [ ] **Step 4: Create `TodoList.Api/Data/ITodoRepository.cs`**

```csharp
using TodoList.Api.Domain;

namespace TodoList.Api.Data;

public interface ITodoRepository
{
    Task<List<Todo>> GetAllAsync(CancellationToken ct = default);
    Task<Todo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Todo todo, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
```

- [ ] **Step 5: Create `TodoList.Api/Data/TodoRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using TodoList.Api.Domain;

namespace TodoList.Api.Data;

public class TodoRepository(TodoDbContext db) : ITodoRepository
{
    public Task<List<Todo>> GetAllAsync(CancellationToken ct) =>
        db.Todos.ToListAsync(ct);

    public Task<Todo?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Todos.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task AddAsync(Todo todo, CancellationToken ct) =>
        await db.Todos.AddAsync(todo, ct);

    public Task SaveAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}
```

- [ ] **Step 6: Create `TodoList.Api/Data/IOperationRepository.cs`**

```csharp
using TodoList.Api.Operations;

namespace TodoList.Api.Data;

public interface IOperationRepository
{
    Task<TodoOperation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(TodoOperation operation, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
```

- [ ] **Step 7: Create `TodoList.Api/Data/OperationRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using TodoList.Api.Operations;

namespace TodoList.Api.Data;

public class OperationRepository(TodoDbContext db) : IOperationRepository
{
    public Task<TodoOperation?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Operations.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task AddAsync(TodoOperation operation, CancellationToken ct) =>
        await db.Operations.AddAsync(operation, ct);

    public Task SaveAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}
```

- [ ] **Step 8: Generate the EF Core migration**

AppHost must be running (which starts SQL Server via Docker) so the design-time factory can connect. If you don't have a local SQL Server, start one manually:

```bash
docker run -e ACCEPT_EULA=Y -e SA_PASSWORD=Password123! -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

Then generate the migration (run from solution root):

```bash
dotnet ef migrations add InitialCreate --project TodoList.Api --startup-project TodoList.Api
```

Expected: creates `TodoList.Api/Migrations/` folder with `InitialCreate` files.

- [ ] **Step 9: Verify migration was generated**

```bash
ls TodoList.Api/Migrations/
```
Expected: `{timestamp}_InitialCreate.cs`, `{timestamp}_InitialCreate.Designer.cs`, `TodoDbContextModelSnapshot.cs`

- [ ] **Step 10: Commit**

```bash
git add TodoList.Api/Data/ TodoList.Api/Operations/ TodoList.Api/Migrations/
git commit -m "feat: add EF Core DbContext, repositories, and InitialCreate migration"
```

---

## Task 10: API — Program.cs + Health Endpoints

- [ ] **Step 1: Write failing integration test for health endpoints** — create `TodoList.IntegrationTests/Api/HealthEndpointsTests.cs`:

```csharp
using System.Net;

namespace TodoList.IntegrationTests.Api;

[Trait("Category", "Integration")]
public class HealthEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task LivenessEndpoint_returns_200()
    {
        var response = await fixture.Client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadinessEndpoint_returns_200_when_db_connected()
    {
        var response = await fixture.Client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: Create `TodoList.IntegrationTests/Fixtures/ApiFixture.cs`**

```csharp
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using TodoList.Api.Data;

namespace TodoList.IntegrationTests.Fixtures;

public class ApiFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                // Override the Aspire connection string so AddSqlServerDbContext("todolist")
                // resolves to the test container instead of the Aspire service catalog.
                // This must be done in ConfigureAppConfiguration, before services are built.
                builder.UseSetting("ConnectionStrings:todolist", _sql.GetConnectionString());
            });

        // Apply EF migrations against the test container DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        await db.Database.MigrateAsync();

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _sql.DisposeAsync();
    }
}
```

- [ ] **Step 3: Create `TodoList.IntegrationTests/GlobalUsings.cs`**

```csharp
global using FluentAssertions;
global using System.Net.Http.Json;
global using System.Text.Json;
global using Xunit;
global using TodoList.IntegrationTests.Fixtures;
```

- [ ] **Step 4: Write `TodoList.Api/Program.cs`**

Note: `/health/live` and `/health/ready` are already mapped by `MapDefaultEndpoints()` from `ServiceDefaults` — no separate `HealthEndpoints.cs` file is needed.

```csharp
using TodoList.Api.Data;
using TodoList.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerDbContext<TodoDbContext>("todolist");

builder.Services.AddScoped<ITodoRepository, TodoRepository>();
builder.Services.AddScoped<IOperationRepository, OperationRepository>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TodoDbContext>(tags: ["ready"]);

builder.Services.AddOpenApi();

var app = builder.Build();

// Auto-migrate in development (production uses CI pipeline)
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    await db.Database.MigrateAsync();
}

app.MapDefaultEndpoints();   // registers /health/live and /health/ready
app.MapTodoEndpoints();
app.MapOperationEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.Run();

// Allow WebApplicationFactory to find Program class
public partial class Program { }
```

Note: `public partial class Program { }` at the bottom is required so `WebApplicationFactory<Program>` can reference the type from the test project.

- [ ] **Step 5: Create stub `TodoList.Api/Endpoints/TodoEndpoints.cs`** so the build succeeds (full implementation in next task):

```csharp
namespace TodoList.Api.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this IEndpointRouteBuilder app) { }
}
```

- [ ] **Step 6: Create stub `TodoList.Api/Endpoints/OperationEndpoints.cs`**:

```csharp
namespace TodoList.Api.Endpoints;

public static class OperationEndpoints
{
    public static void MapOperationEndpoints(this IEndpointRouteBuilder app) { }
}
```

- [ ] **Step 7: Run health integration tests**

```bash
dotnet test TodoList.IntegrationTests --filter "Category=Integration" -v minimal
```
Expected: both health tests pass. (Docker must be running.)

- [ ] **Step 8: Commit**

```bash
git add TodoList.Api/ TodoList.IntegrationTests/
git commit -m "feat: add Api Program.cs, health endpoints, and ApiFixture"
```

---

## Task 11: API — GET Endpoints

- [ ] **Step 1: Write failing integration tests for GET** — create `TodoList.IntegrationTests/Api/TodoEndpointsTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace TodoList.IntegrationTests.Api;

[Trait("Category", "Integration")]
public class TodoEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetTodos_returns_json_array()
    {
        // Tests share a DB and run in undefined order — don't assert empty.
        // This test just verifies the endpoint returns a valid JSON array.
        var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
        todos.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTodo_not_found_returns_404()
    {
        var response = await fixture.Client.GetAsync($"/todos/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Remaining tests added in Task 12
}
```

- [ ] **Step 2: Run tests and verify they fail** (routes not mapped yet)

```bash
dotnet test TodoList.IntegrationTests --filter "Category=Integration" -v minimal
```

- [ ] **Step 3: Implement GET endpoints** — replace stub in `TodoList.Api/Endpoints/TodoEndpoints.cs`:

```csharp
using TodoList.Api.Data;
using TodoList.Api.Domain;

namespace TodoList.Api.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/todos");

        group.MapGet("/", async (ITodoRepository repo, CancellationToken ct) =>
        {
            var todos = await repo.GetAllAsync(ct);
            return Results.Ok(todos.ConvertAll(t => new TodoResponse(t)));
        });

        group.MapGet("/{id:guid}", async (Guid id, ITodoRepository repo, CancellationToken ct) =>
        {
            var todo = await repo.GetByIdAsync(id, ct);
            return todo is null ? Results.NotFound() : Results.Ok(new TodoResponse(todo));
        });
    }
}

public record TodoResponse(
    Guid Id,
    string Title,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public TodoResponse(Todo todo)
        : this(todo.Id, todo.Title, todo.IsCompleted, todo.CreatedAt, todo.CompletedAt) { }
}

public record OperationAcceptedResponse(Guid OperationId, int RetryAfterMs);
```

- [ ] **Step 4: Run GET tests and verify they pass**

```bash
dotnet test TodoList.IntegrationTests --filter "Category=Integration" -v minimal
```

---

## Task 12: API — POST and DELETE Endpoints

- [ ] **Step 1: Add POST and DELETE integration tests** to `TodoEndpointsTests.cs`:

```csharp
[Fact]
public async Task PostTodo_returns_202_with_location_and_retry_header()
{
    var response = await fixture.Client.PostAsJsonAsync("/todos", new { title = "buy milk" });

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    response.Headers.Location.Should().NotBeNull();
    response.Headers.Should().ContainKey("X-Retry-After-Ms");
}

[Fact]
public async Task PostTodo_with_empty_title_returns_400()
{
    var response = await fixture.Client.PostAsJsonAsync("/todos", new { title = "" });
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}

[Fact]
public async Task PostTodo_todo_appears_in_get_list()
{
    // Note: this test relies on the Plan 1 synchronous stub — the todo is persisted
    // immediately in the same request. When Wolverine is added in Plan 3, this test
    // should poll the operation endpoint before asserting the list.
    await fixture.Client.PostAsJsonAsync("/todos", new { title = "integration test todo" });

    var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
    todos!.Should().Contain(t => t.GetProperty("title").GetString() == "integration test todo");
}

[Fact]
public async Task CompleteTodo_returns_202()
{
    // Create a todo
    var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "complete me" });
    var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

    // Poll operation to get the todo ID
    var opResponse = await fixture.Client.GetFromJsonAsync<JsonElement>(
        $"/todos/operations/{created!.OperationId}");
    var todoId = opResponse.GetProperty("result").GetProperty("id").GetGuid();

    // Complete it
    var completeResponse = await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);
    completeResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
}

[Fact]
public async Task CompleteTodo_twice_returns_400()
{
    var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "complete twice" });
    var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
    var opResponse = await fixture.Client.GetFromJsonAsync<JsonElement>(
        $"/todos/operations/{created!.OperationId}");
    var todoId = opResponse.GetProperty("result").GetProperty("id").GetGuid();

    await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);
    var secondComplete = await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);

    secondComplete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}

[Fact]
public async Task UncompleteTodo_after_complete_returns_202()
{
    var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "uncomplete me" });
    var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
    var opResponse = await fixture.Client.GetFromJsonAsync<JsonElement>(
        $"/todos/operations/{created!.OperationId}");
    var todoId = opResponse.GetProperty("result").GetProperty("id").GetGuid();

    await fixture.Client.PostAsync($"/todos/{todoId}/complete", null);
    var uncompleteResponse = await fixture.Client.PostAsync($"/todos/{todoId}/uncomplete", null);

    uncompleteResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
}

[Fact]
public async Task DeleteTodo_returns_202_and_todo_disappears_from_list()
{
    var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "delete me" });
    var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
    var opResponse = await fixture.Client.GetFromJsonAsync<JsonElement>(
        $"/todos/operations/{created!.OperationId}");
    var todoId = opResponse.GetProperty("result").GetProperty("id").GetGuid();

    var deleteResponse = await fixture.Client.DeleteAsync($"/todos/{todoId}");
    deleteResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

    var todos = await fixture.Client.GetFromJsonAsync<JsonElement[]>("/todos");
    todos!.Should().NotContain(t => t.GetProperty("id").GetGuid() == todoId);
}
```

Note: these tests use `OperationAcceptedResponse` from `TodoList.Api.Endpoints`. Add to `GlobalUsings.cs`:
```csharp
global using TodoList.Api.Endpoints;
```

- [ ] **Step 2: Run tests and verify they fail** (POST/DELETE not implemented yet)

```bash
dotnet test TodoList.IntegrationTests --filter "Category=Integration" -v minimal
```

- [ ] **Step 3: Implement POST and DELETE** — add to `TodoEndpoints.cs`

**Important shared-DbContext save semantics:** `ITodoRepository` and `IOperationRepository` both inject the same scoped `TodoDbContext`. Calling `SaveAsync()` on either repository calls `db.SaveChangesAsync()` which flushes **all** staged changes — including entities staged via the other repository. The convention in these handlers is: stage all changes with `AddAsync`, then call `ops.SaveAsync(ct)` once at the end to flush everything in one transaction.

**Plan 1 simplification:** Operations are written with `Status = "complete"` synchronously within the HTTP handler. The full `pending → processing → complete` lifecycle (driven by Wolverine) is implemented in Plan 3. The HTTP contract (202 + Location + `X-Retry-After-Ms`) is identical regardless.

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TodoList.Api.Data;
using TodoList.Api.Domain;
using TodoList.Api.Operations;

// Inside MapTodoEndpoints, after the GET endpoints:

group.MapPost("/", async (
    [FromBody] CreateTodoRequest request,
    ITodoRepository todos,
    IOperationRepository ops,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var result = Todo.Create(request.Title, DateTimeOffset.UtcNow);
    if (!result.IsSuccess)
        return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["title"] = result.Errors });

    var (todo, _) = result.Value!;
    var operation = new TodoOperation
    {
        Id          = Guid.NewGuid(),
        Status      = "complete",
        ResultJson  = JsonSerializer.Serialize(new { id = todo.Id }),
        CreatedAt   = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    await todos.AddAsync(todo, ct);  // stages Todo
    await ops.AddAsync(operation, ct);  // stages Operation
    await ops.SaveAsync(ct);  // flushes both (shared DbContext)

    httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
    return Results.Accepted(
        $"/todos/operations/{operation.Id}",
        new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
});

group.MapPost("/{id:guid}/complete", async (
    Guid id,
    ITodoRepository todos,
    IOperationRepository ops,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var todo = await todos.GetByIdAsync(id, ct);
    if (todo is null) return Results.NotFound();

    var result = todo.Complete(DateTimeOffset.UtcNow);
    if (!result.IsSuccess)
        return Results.ValidationProblem(
            new Dictionary<string, string[]> { [""] = result.Errors });

    var operation = new TodoOperation
    {
        Id          = Guid.NewGuid(),
        Status      = "complete",
        CreatedAt   = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    await ops.AddAsync(operation, ct);
    await ops.SaveAsync(ct);  // flushes all (shared DbContext — saves todo state change + operation)

    httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
    return Results.Accepted(
        $"/todos/operations/{operation.Id}",
        new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
});

group.MapPost("/{id:guid}/uncomplete", async (
    Guid id,
    ITodoRepository todos,
    IOperationRepository ops,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var todo = await todos.GetByIdAsync(id, ct);
    if (todo is null) return Results.NotFound();

    var result = todo.Uncomplete();
    if (!result.IsSuccess)
        return Results.ValidationProblem(
            new Dictionary<string, string[]> { [""] = result.Errors });

    var operation = new TodoOperation
    {
        Id          = Guid.NewGuid(),
        Status      = "complete",
        CreatedAt   = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    await ops.AddAsync(operation, ct);
    await ops.SaveAsync(ct);  // flushes all (shared DbContext — saves todo state change + operation)

    httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
    return Results.Accepted(
        $"/todos/operations/{operation.Id}",
        new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
});

group.MapDelete("/{id:guid}", async (
    Guid id,
    ITodoRepository todos,
    IOperationRepository ops,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var todo = await todos.GetByIdAsync(id, ct);
    if (todo is null) return Results.NotFound();

    var result = todo.Delete(DateTimeOffset.UtcNow);
    if (!result.IsSuccess)
        return Results.ValidationProblem(
            new Dictionary<string, string[]> { [""] = result.Errors });

    var operation = new TodoOperation
    {
        Id          = Guid.NewGuid(),
        Status      = "complete",
        CreatedAt   = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    await ops.AddAsync(operation, ct);
    await ops.SaveAsync(ct);  // flushes all (shared DbContext — saves todo state change + operation)

    httpContext.Response.Headers["X-Retry-After-Ms"] = "200";
    return Results.Accepted(
        $"/todos/operations/{operation.Id}",
        new OperationAcceptedResponse(operation.Id, RetryAfterMs: 200));
});
```

Also add the request record at the bottom of `TodoEndpoints.cs`:

```csharp
public record CreateTodoRequest(string Title);
```

- [ ] **Step 4: Run all integration tests and verify they pass**

```bash
dotnet test TodoList.IntegrationTests --filter "Category=Integration" -v minimal
```

---

## Task 13: API — Operation Endpoint

- [ ] **Step 1: Write failing test** — create `TodoList.IntegrationTests/Api/OperationEndpointsTests.cs`:

```csharp
using System.Net;

namespace TodoList.IntegrationTests.Api;

[Trait("Category", "Integration")]
public class OperationEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetOperation_after_create_returns_complete_with_todo_id()
    {
        var createResponse = await fixture.Client.PostAsJsonAsync("/todos", new { title = "op test" });
        var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        var opResponse = await fixture.Client.GetFromJsonAsync<JsonElement>(
            $"/todos/operations/{created!.OperationId}");

        opResponse.GetProperty("status").GetString().Should().Be("complete");
        opResponse.GetProperty("result").GetProperty("id").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetOperation_not_found_returns_404()
    {
        var response = await fixture.Client.GetAsync($"/todos/operations/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

```bash
dotnet test TodoList.IntegrationTests --filter "Category=Integration" -v minimal
```

- [ ] **Step 3: Implement `OperationEndpoints.cs`**

```csharp
using System.Text.Json;
using TodoList.Api.Data;

namespace TodoList.Api.Endpoints;

public static class OperationEndpoints
{
    public static void MapOperationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/todos/operations/{id:guid}", async (
            Guid id,
            IOperationRepository ops,
            CancellationToken ct) =>
        {
            var operation = await ops.GetByIdAsync(id, ct);
            if (operation is null) return Results.NotFound();

            JsonElement? result = operation.ResultJson is not null
                ? JsonSerializer.Deserialize<JsonElement>(operation.ResultJson)
                : null;

            return Results.Ok(new
            {
                id            = operation.Id,
                status        = operation.Status,
                result,
                failureReason = operation.FailureReason,
                isRetryable   = operation.IsRetryable,
                createdAt     = operation.CreatedAt,
                completedAt   = operation.CompletedAt
            });
        });
    }
}
```

- [ ] **Step 4: Run all tests and verify they all pass**

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v minimal
dotnet test TodoList.IntegrationTests --filter "Category=Integration" -v minimal
```
Expected: 17 unit tests + all integration tests green.

- [ ] **Step 5: Commit**

```bash
git add TodoList.Api/Endpoints/ TodoList.IntegrationTests/
git commit -m "feat: add REST API endpoints with async operation pattern"
```

---

## Task 14: Stub Web, Mcp.Tools, Mcp.Composite

These stubs ensure the solution builds cleanly and AppHost can reference all projects.

- [ ] **Step 1: Replace `TodoList.Web/Program.cs`**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => "TodoList Web — coming in Plan 2");
app.Run();
```

- [ ] **Step 2: Replace `TodoList.Mcp.Tools/Program.cs`**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => "TodoList MCP Tools — coming in Plan 5");
app.Run();
```

- [ ] **Step 3: Replace `TodoList.Mcp.Composite/Program.cs`**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => "TodoList MCP Composite — coming in Plan 6");
app.Run();
```

- [ ] **Step 4: Verify solution builds cleanly**

```bash
dotnet build TodoList.sln
```
Expected: no errors.

---

## Task 15: Final Verification + Commit

- [ ] **Step 1: Run all unit tests**

```bash
dotnet test TodoList.Tests --filter "Category=Unit" -v normal
```
Expected: 17 tests pass in < 2 seconds.

- [ ] **Step 2: Run all integration tests**

```bash
dotnet test TodoList.IntegrationTests --filter "Category=Integration" -v normal
```
Expected: all tests pass (Docker must be running).

- [ ] **Step 3: Run the AppHost locally** to verify Aspire wiring

```bash
dotnet run --project TodoList.AppHost
```
Expected: Aspire Dashboard opens at `http://localhost:18888`, SQL Server container starts, Api is reachable.

- [ ] **Step 4: Smoke test the API via curl**

```bash
# Create a todo
curl -s -X POST http://localhost:5000/todos \
  -H "Content-Type: application/json" \
  -d '{"title":"smoke test"}' | jq .

# List todos
curl -s http://localhost:5000/todos | jq .

# Check health
curl -s http://localhost:5000/health/ready
```

- [ ] **Step 5: Final commit**

```bash
git add .
git commit -m "feat: complete Plan 1 foundation — domain model, EF Core, REST API, tests"
```

---

## Definition of Done

- [ ] `dotnet test TodoList.Tests --filter "Category=Unit"` → 17 tests, < 2 seconds
- [ ] `dotnet test TodoList.IntegrationTests --filter "Category=Integration"` → all green
- [ ] `dotnet build TodoList.sln` → no errors
- [ ] `dotnet run --project TodoList.AppHost` → Aspire Dashboard, Api reachable, health/ready = 200
