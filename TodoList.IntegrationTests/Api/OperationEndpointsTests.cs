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

        var opResponse = await fixture.PollOperationAsync(created!.OperationId);

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
