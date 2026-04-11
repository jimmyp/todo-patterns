using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TodoList.Domain.ReadModels;

namespace TodoList.IntegrationTests.Categories;

public class CategoryEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetCategories_returns_seeded_categories_for_new_user()
    {
        // Seed categories for test user by calling the API
        var seedResponse = await fixture.Client.PostAsJsonAsync("/categories/seed", new { });
        seedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await fixture.Client.GetAsync("/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var categories = await response.Content.ReadFromJsonAsync<CategorySummary[]>();
        categories.Should().HaveCount(4);
        categories!.Select(c => c.Name).Should().BeEquivalentTo(["Personal", "Work", "Urgent", "Design"]);
    }

    [Fact]
    public async Task PostCategory_returns_202_and_creates_category()
    {
        await fixture.Client.PostAsJsonAsync("/categories/seed", new { });
        var response = await fixture.Client.PostAsJsonAsync("/categories",
            new { name = "Hobby", color = "#FF0000", icon = "star" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task PostCategory_returns_422_for_invalid_color()
    {
        await fixture.Client.PostAsJsonAsync("/categories/seed", new { });
        var response = await fixture.Client.PostAsJsonAsync("/categories",
            new { name = "Hobby", color = "notacolor", icon = "star" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task DeleteCategory_returns_202()
    {
        await fixture.Client.PostAsJsonAsync("/categories/seed", new { });
        var cats = await fixture.Client.GetFromJsonAsync<CategorySummary[]>("/categories");
        var id = cats![0].Id;

        var response = await fixture.Client.DeleteAsync($"/categories/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
