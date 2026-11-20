using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace AppTemplate.Api.Tests.Data;

/// <summary>The History table is framework, not example: these tests survive skill:remove-example.</summary>
public sealed class HistoryPersistenceTests
{
    [Fact]
    public async Task History_RoundTripsEveryColumn()
    {
        using var db = new SqliteTestDatabase();
        db.Migrate();
        var occurredAt = new DateTimeOffset(2026, 11, 20, 10, 30, 0, TimeSpan.Zero);
        var executionId = Guid.NewGuid();
        var written = new HistoryEntry(
            aggregateType: "Project",
            aggregateId: "0f8fad5b-d9cb-469f-a165-70867728950e",
            aggregateVersion: 3,
            eventType: "project.renamed",
            eventVersion: 1,
            payload: """{"oldName":"A","newName":"B"}""",
            occurredAt: occurredAt,
            executionId: executionId,
            action: "rename_project",
            channel: "mcp",
            client: "claude-code",
            reason: "customer asked",
            traceId: "4bf92f3577b34da6a3ce929d0e0e4736");
        await using (var c = db.NewContext())
        {
            c.History.Add(written);
            await c.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var c2 = db.NewContext();
        var read = await c2.History.SingleAsync(TestContext.Current.CancellationToken);

        read.Id.ShouldBeGreaterThan(0);
        read.AggregateType.ShouldBe("Project");
        read.AggregateId.ShouldBe("0f8fad5b-d9cb-469f-a165-70867728950e");
        read.AggregateVersion.ShouldBe(3);
        read.EventType.ShouldBe("project.renamed");
        read.EventVersion.ShouldBe(1);
        read.Payload.ShouldBe("""{"oldName":"A","newName":"B"}""");
        read.OccurredAt.ShouldBe(occurredAt);
        read.ExecutionId.ShouldBe(executionId);
        read.Action.ShouldBe("rename_project");
        read.Channel.ShouldBe("mcp");
        read.Client.ShouldBe("claude-code");
        read.Reason.ShouldBe("customer asked");
        read.TraceId.ShouldBe("4bf92f3577b34da6a3ce929d0e0e4736");
    }
}
