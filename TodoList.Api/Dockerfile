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
