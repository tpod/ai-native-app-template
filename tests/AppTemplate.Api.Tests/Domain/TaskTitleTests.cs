using AppTemplate.Domain.Projects;
using Shouldly;

namespace AppTemplate.Api.Tests.Domain;

public sealed class TaskTitleTests
{
    [Fact]
    public void Create_PaddedTitle_Trims()
    {
        var result = TaskTitle.Create("  Write docs  ");
        result.IsT0.ShouldBeTrue();
        result.AsT0.Value.ShouldBe("Write docs");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Whitespace_ReturnsEmpty(string? raw)
    {
        var result = TaskTitle.Create(raw);
        result.IsT1.ShouldBeTrue();
        result.AsT1.Code.ShouldBe("task_title.empty");
    }

    [Fact]
    public void Create_200Chars_Succeeds()
        => TaskTitle.Create(new string('a', 200)).IsT0.ShouldBeTrue();

    [Fact]
    public void Create_201Chars_ReturnsTooLong()
    {
        var result = TaskTitle.Create(new string('a', 201));
        result.IsT1.ShouldBeTrue();
        result.AsT1.Code.ShouldBe("task_title.too_long");
        result.AsT1.Message.ShouldContain("200");
    }
}
