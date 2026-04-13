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
