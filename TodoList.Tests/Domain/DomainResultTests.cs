namespace TodoList.Tests.Domain;

[Trait("Category", "Unit")]
public class DomainResultTests
{
    [Fact]
    public void Ok_IsSuccess_true_and_has_value()
    {
        var result = DomainResult<int>.Ok(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Fail_IsSuccess_false_and_has_errors()
    {
        var result = DomainResult<int>.Fail("error one", "error two");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().BeEquivalentTo(["error one", "error two"]);
        result.Value.Should().Be(default);
    }
}
