using AppTemplate.Api.Actions;
using Shouldly;

namespace AppTemplate.Api.Tests.History;

public sealed class ActionCallerTests
{
    [Fact]
    public void Mcp_ClientNameLongerThanColumn_IsTruncated()
    {
        var caller = ActionCaller.Mcp(new string('c', 300));

        caller.Client!.Length.ShouldBe(200);
    }

    [Fact]
    public void Mcp_BlankClientName_IsNull()
        => ActionCaller.Mcp("  ").Client.ShouldBeNull();
}
