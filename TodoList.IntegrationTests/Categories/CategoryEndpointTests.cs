using System.Net;
using TodoList.Domain.ReadModels;

namespace TodoList.IntegrationTests.Categories;

[Collection(ApiCollection.Name)]
public class CategoryEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetCategories_returns_seeded_categories_for_new_user()
    {
        // Auto-seed on first GET: the server creates a CategoryList with defaults.
        // Endpoint returns { version, categories: [...] } — version carries the
        // CategoryList aggregate version for ExpectedVersion on subsequent commands.
        var response = await fixture.Client.GetAsync("/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var categories = body.GetProperty("categories").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()!)
            .ToList();
        categories.Should().HaveCountGreaterThanOrEqualTo(4);
        categories.Should().Contain(["Personal", "Work", "Urgent", "Design"]);
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
        var body = await fixture.Client.GetFromJsonAsync<JsonElement>("/categories");
        var id = body.GetProperty("categories")[0].GetProperty("id").GetGuid();

        var response = await fixture.Client.DeleteAsync($"/categories/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // First GET triggers auto-seed server-side. Tests that mutate categories call this
    // first so the CategoryList aggregate exists before issuing the command.
    private async Task EnsureSeeded() =>
        await fixture.Client.GetAsync("/categories");
}
