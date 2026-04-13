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
