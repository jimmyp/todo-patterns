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
