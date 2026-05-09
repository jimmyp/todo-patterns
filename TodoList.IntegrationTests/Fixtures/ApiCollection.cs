namespace TodoList.IntegrationTests.Fixtures;

/// <summary>
/// xUnit collection that shares a single ApiFixture (and therefore one SQL Server
/// Testcontainer + WebApplicationFactory) across all integration test classes.
/// Without this, every IClassFixture<ApiFixture> spins up its own container,
/// quickly exhausting Docker memory once the suite grows past a handful of classes.
/// </summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "ApiCollection";
}
