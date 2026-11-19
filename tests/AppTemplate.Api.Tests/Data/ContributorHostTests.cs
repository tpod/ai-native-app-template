using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Data;

public sealed class ContributorHostTests : ApiTestBase
{
    // Without the Notes fixture, so this test's own contributor registration is the only one.
    protected override ApiFactory CreateFactory() => new(includeFixtures: false);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IModelContributor, NoteModelContributor>();

    [Fact]
    public async Task Host_WithNoteContributorInDi_BuildsAndPersistsNote()
    {
        var id = NoteId.From(Guid.NewGuid());
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<Note>().Add(Note.Create(id, NoteText.Create("hello").AsT0));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var scope2 = Factory.Services.CreateScope();
        var read = await scope2.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<Note>().SingleAsync(n => n.Id == id, TestContext.Current.CancellationToken);
        read.Text.Value.ShouldBe("hello");
    }
}
