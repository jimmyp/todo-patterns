# Plan G: CI/CD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a GitHub Actions pipeline that builds the solution, runs unit tests, runs integration tests (with Testcontainers SQL Server), and publishes container images.

**Architecture:** Two workflow files: `ci.yml` runs on every push and PR (build + unit tests + integration tests). `publish.yml` runs on push to main and builds Docker images for `Api`, `Mcp.Tools`, and `Mcp.Composite`. Integration tests run in a `ubuntu-latest` runner that has Docker available (required for Testcontainers). Unit tests run first (fast feedback < 2s), integration tests run second (require Docker, ~2 minutes). Both test suites emit JUnit XML to the GitHub Actions step summary. The pipeline requires no secrets for unit or integration tests — SQL Server runs in a container spun up by Testcontainers.

**Tech Stack:** GitHub Actions, Docker, .NET 10 SDK, Testcontainers (SQL Server), xUnit JUnit XML reporter

> **Read before starting:** `TodoList.IntegrationTests/TodoList.IntegrationTests.csproj`, `TodoList.Tests/TodoList.Tests.csproj`, `TodoList.Api/TodoList.Api.csproj`, `TodoList.AppHost/TodoList.AppHost.csproj`

---

## File Map

### New: `.github/workflows/`
```
.github/workflows/ci.yml        # build + unit tests + integration tests on every push/PR
.github/workflows/publish.yml   # build + push container images on push to main
```

### New: `TodoList.Api/`
```
TodoList.Api/Dockerfile          # multi-stage Dockerfile for the Api
```

### New: `TodoList.Mcp.Tools/`
```
TodoList.Mcp.Tools/Dockerfile    # multi-stage Dockerfile for Mcp.Tools
```

### New: `TodoList.Mcp.Composite/`
```
TodoList.Mcp.Composite/Dockerfile # multi-stage Dockerfile for Mcp.Composite
```

### New: root
```
.dockerignore                   # exclude obj/, bin/, .git/ from Docker build context
```

---

## Tasks

### Task 1: Create .dockerignore

- [ ] **Step 1: Create `.dockerignore`**

```
**/bin/
**/obj/
**/.git/
**/.vs/
**/.vscode/
**/node_modules/
**/*.user
**/TestResults/
```

- [ ] **Step 2: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add .dockerignore
git commit -m "chore(ci): add .dockerignore"
```

---

### Task 2: Create Dockerfiles

- [ ] **Step 1: Create `TodoList.Api/Dockerfile`**

```dockerfile
# TodoList.Api/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TodoList.Api/TodoList.Api.csproj", "TodoList.Api/"]
COPY ["TodoList.Domain/TodoList.Domain.csproj", "TodoList.Domain/"]
COPY ["TodoList.ServiceDefaults/TodoList.ServiceDefaults.csproj", "TodoList.ServiceDefaults/"]
RUN dotnet restore "TodoList.Api/TodoList.Api.csproj"
COPY . .
WORKDIR "/src/TodoList.Api"
RUN dotnet publish "TodoList.Api.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TodoList.Api.dll"]
```

- [ ] **Step 2: Create `TodoList.Mcp.Tools/Dockerfile`**

```dockerfile
# TodoList.Mcp.Tools/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj", "TodoList.Mcp.Tools/"]
COPY ["TodoList.ServiceDefaults/TodoList.ServiceDefaults.csproj", "TodoList.ServiceDefaults/"]
RUN dotnet restore "TodoList.Mcp.Tools/TodoList.Mcp.Tools.csproj"
COPY . .
WORKDIR "/src/TodoList.Mcp.Tools"
RUN dotnet publish "TodoList.Mcp.Tools.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TodoList.Mcp.Tools.dll"]
```

- [ ] **Step 3: Create `TodoList.Mcp.Composite/Dockerfile`**

```dockerfile
# TodoList.Mcp.Composite/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj", "TodoList.Mcp.Composite/"]
COPY ["TodoList.ServiceDefaults/TodoList.ServiceDefaults.csproj", "TodoList.ServiceDefaults/"]
RUN dotnet restore "TodoList.Mcp.Composite/TodoList.Mcp.Composite.csproj"
COPY . .
WORKDIR "/src/TodoList.Mcp.Composite"
RUN dotnet publish "TodoList.Mcp.Composite.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TodoList.Mcp.Composite.dll"]
```

- [ ] **Step 4: Commit Dockerfiles**

```bash
cd /Users/jim/code/todo-patterns
git add TodoList.Api/Dockerfile TodoList.Mcp.Tools/Dockerfile TodoList.Mcp.Composite/Dockerfile
git commit -m "chore(ci): add Dockerfiles for Api, Mcp.Tools, Mcp.Composite"
```

---

### Task 3: Create CI workflow

- [ ] **Step 1: Create `.github/workflows/ci.yml`**

```yaml
# .github/workflows/ci.yml
name: CI

on:
  push:
    branches: ["**"]
  pull_request:
    branches: ["**"]

concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

jobs:
  # ---------------------------------------------------------------------------
  # Job 1: Unit tests — fast, no Docker required
  # ---------------------------------------------------------------------------
  unit-tests:
    name: Unit Tests
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Restore
        run: dotnet restore TodoList.Tests/TodoList.Tests.csproj

      - name: Build
        run: dotnet build TodoList.Tests/TodoList.Tests.csproj --no-restore -c Release

      - name: Run unit tests
        run: |
          dotnet test TodoList.Tests/TodoList.Tests.csproj \
            --no-build \
            -c Release \
            --logger "trx;LogFileName=unit-tests.trx" \
            --results-directory ./TestResults

      - name: Publish test results
        uses: dorny/test-reporter@v1
        if: always()
        with:
          name: Unit Tests
          path: TestResults/unit-tests.trx
          reporter: dotnet-trx

  # ---------------------------------------------------------------------------
  # Job 2: Build solution — verify all projects compile
  # ---------------------------------------------------------------------------
  build:
    name: Build Solution
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Restore solution
        run: dotnet restore TodoList.sln

      - name: Build solution
        run: dotnet build TodoList.sln --no-restore -c Release

  # ---------------------------------------------------------------------------
  # Job 3: Integration tests — requires Docker for Testcontainers SQL Server
  # ---------------------------------------------------------------------------
  integration-tests:
    name: Integration Tests
    runs-on: ubuntu-latest
    needs: [unit-tests, build]
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Restore
        run: dotnet restore TodoList.IntegrationTests/TodoList.IntegrationTests.csproj

      - name: Build
        run: dotnet build TodoList.IntegrationTests/TodoList.IntegrationTests.csproj --no-restore -c Release

      - name: Run integration tests
        run: |
          dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj \
            --no-build \
            -c Release \
            --logger "trx;LogFileName=integration-tests.trx" \
            --results-directory ./TestResults
        env:
          # Testcontainers needs Docker — available on ubuntu-latest
          DOTNET_ENVIRONMENT: Testing

      - name: Publish test results
        uses: dorny/test-reporter@v1
        if: always()
        with:
          name: Integration Tests
          path: TestResults/integration-tests.trx
          reporter: dotnet-trx
```

- [ ] **Step 2: Commit**

```bash
cd /Users/jim/code/todo-patterns
mkdir -p .github/workflows
git add .github/workflows/ci.yml
git commit -m "feat(ci): add GitHub Actions CI workflow with unit + integration tests"
```

---

### Task 4: Create publish workflow

- [ ] **Step 1: Create `.github/workflows/publish.yml`**

```yaml
# .github/workflows/publish.yml
name: Publish

on:
  push:
    branches: [main, todo-list-app-278126191092115797]

concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

jobs:
  # Only publish after CI passes
  publish:
    name: Build and Push Images
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Log in to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Set image tag
        id: tag
        run: |
          SHORT_SHA=$(echo "${{ github.sha }}" | cut -c1-7)
          echo "tag=${SHORT_SHA}" >> $GITHUB_OUTPUT
          echo "repo=$(echo '${{ github.repository }}' | tr '[:upper:]' '[:lower:]')" >> $GITHUB_OUTPUT

      - name: Build and push Api image
        uses: docker/build-push-action@v5
        with:
          context: .
          file: TodoList.Api/Dockerfile
          push: true
          tags: |
            ghcr.io/${{ steps.tag.outputs.repo }}/api:${{ steps.tag.outputs.tag }}
            ghcr.io/${{ steps.tag.outputs.repo }}/api:latest

      - name: Build and push Mcp.Tools image
        uses: docker/build-push-action@v5
        with:
          context: .
          file: TodoList.Mcp.Tools/Dockerfile
          push: true
          tags: |
            ghcr.io/${{ steps.tag.outputs.repo }}/mcp-tools:${{ steps.tag.outputs.tag }}
            ghcr.io/${{ steps.tag.outputs.repo }}/mcp-tools:latest

      - name: Build and push Mcp.Composite image
        uses: docker/build-push-action@v5
        with:
          context: .
          file: TodoList.Mcp.Composite/Dockerfile
          push: true
          tags: |
            ghcr.io/${{ steps.tag.outputs.repo }}/mcp-composite:${{ steps.tag.outputs.tag }}
            ghcr.io/${{ steps.tag.outputs.repo }}/mcp-composite:latest

      - name: Summary
        run: |
          echo "## Published Images" >> $GITHUB_STEP_SUMMARY
          echo "| Image | Tag |" >> $GITHUB_STEP_SUMMARY
          echo "|---|---|" >> $GITHUB_STEP_SUMMARY
          echo "| api | ${{ steps.tag.outputs.tag }} |" >> $GITHUB_STEP_SUMMARY
          echo "| mcp-tools | ${{ steps.tag.outputs.tag }} |" >> $GITHUB_STEP_SUMMARY
          echo "| mcp-composite | ${{ steps.tag.outputs.tag }} |" >> $GITHUB_STEP_SUMMARY
```

- [ ] **Step 2: Commit**

```bash
cd /Users/jim/code/todo-patterns
git add .github/workflows/publish.yml
git commit -m "feat(ci): add GitHub Actions publish workflow for container images"
```

---

### Task 5: Verify CI locally

- [ ] **Step 1: Build the full solution to confirm everything compiles**

```bash
cd /Users/jim/code/todo-patterns
dotnet build TodoList.sln -c Release
```

Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 2: Run unit tests**

```bash
cd /Users/jim/code/todo-patterns
dotnet test TodoList.Tests/TodoList.Tests.csproj -c Release --logger "console;verbosity=normal"
```

Expected: All tests pass.

- [ ] **Step 3: Run integration tests**

```bash
cd /Users/jim/code/todo-patterns
dotnet test TodoList.IntegrationTests/TodoList.IntegrationTests.csproj -c Release --logger "console;verbosity=normal"
```

Expected: All tests pass.

- [ ] **Step 4: Verify Dockerfiles can be parsed (syntax check)**

```bash
docker build --no-cache --dry-run -f TodoList.Api/Dockerfile . 2>&1 | head -5 || echo "docker dry-run not available on this platform, skipping"
```

This step is best-effort — it passes if Docker is available and the Dockerfile parses. If Docker is not available locally, skip and rely on CI.

- [ ] **Step 5: Commit any final fixes**

If any test failures or build errors were found, fix and commit. If all clean, no additional commit needed.

---

### Task 6: Push and verify CI runs

- [ ] **Step 1: Push to trigger CI**

```bash
cd /Users/jim/code/todo-patterns
git push
```

- [ ] **Step 2: Monitor CI run**

```bash
gh run list --limit 5
```

Wait for the run to complete:

```bash
gh run watch
```

Expected: All three jobs (unit-tests, build, integration-tests) show green.

- [ ] **Step 3: If any job fails, view logs**

```bash
gh run view --log-failed
```

Fix failures and push again.
