using AppTemplate.Domain.Projects;
using Shouldly;

namespace AppTemplate.Api.Tests.Domain;

public sealed class ProjectNameTests
{
    [Fact]
    public void Create_PaddedName_Trims()
    {
        var result = ProjectName.Create("  Alpha  ");
        result.IsT0.ShouldBeTrue();
        result.AsT0.Value.ShouldBe("Alpha");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Whitespace_ReturnsEmpty(string? raw)
    {
        var result = ProjectName.Create(raw);
        result.IsT1.ShouldBeTrue();
        result.AsT1.Code.ShouldBe("project_name.empty");
    }

    [Fact]
    public void Create_100Chars_Succeeds()
        => ProjectName.Create(new string('a', 100)).IsT0.ShouldBeTrue();

    [Fact]
    public void Create_101Chars_ReturnsTooLong()
    {
        var result = ProjectName.Create(new string('a', 101));
        result.IsT1.ShouldBeTrue();
        result.AsT1.Code.ShouldBe("project_name.too_long");
        result.AsT1.Message.ShouldContain("100");
    }
}
