using System.Net;
using TodoList.Domain.ReadModels;

namespace TodoList.IntegrationTests.Categories;

public class CategoryEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetCategories_returns_seeded_categories_for_new_user()
    {
        // Seed categories via Wolverine command
        var seedResponse = await fixture.Client.PostAsync("/categories/seed", null);
        seedResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var seedAccepted = await seedResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        await fixture.PollOperationAsync(seedAccepted!.OperationId);

        var response = await fixture.Client.GetAsync("/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var categories = await response.Content.ReadFromJsonAsync<CategorySummary[]>();
        categories.Should().HaveCountGreaterThanOrEqualTo(4);
        categories!.Select(c => c.Name).Should().Contain(["Personal", "Work", "Urgent", "Design"]);
    }

    [Fact]
    public async Task PostCategory_returns_202_and_creates_category()
    {
        await EnsureSeeded();
        var response = await fixture.Client.PostAsJsonAsync("/categories",
            new { name = "Hobby", color = "#FF0000", icon = "star" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task PostCategory_with_invalid_color_fails_operation()
    {
        await EnsureSeeded();
        var response = await fixture.Client.PostAsJsonAsync("/categories",
            new { name = "BadColor", color = "notacolor", icon = "star" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var op = await fixture.PollOperationAsync(accepted!.OperationId);
        op.GetProperty("status").GetString().Should().Be("failed");
    }

    [Fact]
    public async Task DeleteCategory_returns_202()
    {
        await EnsureSeeded();
        var cats = await fixture.Client.GetFromJsonAsync<CategorySummary[]>("/categories");
        var id = cats![0].Id;

        var response = await fixture.Client.DeleteAsync($"/categories/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    private async Task EnsureSeeded()
    {
        var seedResponse = await fixture.Client.PostAsync("/categories/seed", null);
        if (seedResponse.StatusCode == HttpStatusCode.Accepted)
        {
            var accepted = await seedResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
            await fixture.PollOperationAsync(accepted!.OperationId);
        }
    }
}
