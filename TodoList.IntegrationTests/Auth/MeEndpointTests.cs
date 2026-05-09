using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TodoList.IntegrationTests.Fixtures;

namespace TodoList.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public class MeEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetMe_ReturnsTestUser()
    {
        var response = await fixture.Client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        body.Should().NotBeNull();
        body!.UserId.Should().Be(TestAuthHandler.TestUserId);
        body.Email.Should().Be("test@example.com");
        body.AuthMethod.Should().Be("test");
    }

    private record MeResponse(string UserId, string Email, string? Name, string AuthMethod);
}
