using TodoList.Domain.Aggregates;
using TodoList.Domain.Events;
using FluentAssertions;

namespace TodoList.Tests;

public class CategoryListTests
{
    [Fact]
    public void Create_seeds_four_default_categories()
    {
        var (list, events) = CategoryList.Create("user-1");

        list.Categories.Should().HaveCount(4);
        events.Should().HaveCount(4);
        list.Categories.Select(c => c.Name).Should()
            .BeEquivalentTo(["Personal", "Work", "Urgent", "Design"]);
    }

    [Fact]
    public void AddCategory_succeeds_with_unique_name()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory("Hobby", "#FF0000", "star");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Hobby");
        list.Categories.Should().HaveCount(5);
    }

    [Fact]
    public void AddCategory_fails_when_name_already_exists()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory("Personal", "#FF0000", "star");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("already exists"));
    }

    [Fact]
    public void AddCategory_fails_when_name_is_empty()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory("", "#FF0000", "star");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("required"));
    }

    [Fact]
    public void AddCategory_fails_when_name_exceeds_50_chars()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory(new string('x', 51), "#FF0000", "star");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("50"));
    }

    [Fact]
    public void AddCategory_increments_version()
    {
        var (list, _) = CategoryList.Create("user-1");
        var before = list.Version;
        list.AddCategory("Hobby", "#FF0000", "star");

        list.Version.Should().Be(before + 1);
    }

    [Fact]
    public void AddCategory_fails_when_color_is_invalid()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.AddCategory("Hobby", "notacolor", "star");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("color"));
    }

    [Fact]
    public void RenameCategory_succeeds()
    {
        var (list, _) = CategoryList.Create("user-1");
        var personal = list.Categories.First(c => c.Name == "Personal");
        var result = list.RenameCategory(personal.Id, "Home");

        result.IsSuccess.Should().BeTrue();
        result.Value!.NewName.Should().Be("Home");
        list.Categories.First(c => c.Id == personal.Id).Name.Should().Be("Home");
    }

    [Fact]
    public void RenameCategory_fails_when_new_name_conflicts()
    {
        var (list, _) = CategoryList.Create("user-1");
        var personal = list.Categories.First(c => c.Name == "Personal");
        var result = list.RenameCategory(personal.Id, "Work");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("already exists"));
    }

    [Fact]
    public void RemoveCategory_succeeds()
    {
        var (list, _) = CategoryList.Create("user-1");
        var personal = list.Categories.First(c => c.Name == "Personal");
        var result = list.RemoveCategory(personal.Id);

        result.IsSuccess.Should().BeTrue();
        list.Categories.Should().HaveCount(3);
    }

    [Fact]
    public void RemoveCategory_fails_when_not_found()
    {
        var (list, _) = CategoryList.Create("user-1");
        var result = list.RemoveCategory(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }
}
