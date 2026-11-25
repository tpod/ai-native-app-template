using AppTemplate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace AppTemplate.Api.PostgresTests;

/// <summary><c>Version</c> is the concurrency token that guards every execute's save; prove Postgres enforces it.</summary>
public sealed class ConcurrencyTokenTests(PostgresContainerFixture postgres) : PostgresTestBase(postgres)
{
    [Fact]
    public async Task SaveChanges_ProjectChangedByOtherContext_ThrowsConcurrencyException()
    {
        await PreviewAndExecute("create_project", new { name = "Alpha" });
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Projects.SingleAsync(Ct);
        var b = await second.Projects.SingleAsync(Ct);

        ((IAggregateRoot)a).IncrementVersion();
        await first.SaveChangesAsync(Ct);
        ((IAggregateRoot)b).IncrementVersion();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
    }
}
