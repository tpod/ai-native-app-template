using AppTemplate.Api.Actions;
using Microsoft.EntityFrameworkCore;
using OneOf;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

/// <param name="Locked">Only notes with this locked state; null (or omitted) lists every note.</param>
public sealed record ListNotesInput(bool? Locked = null);

[Query("fixture_list_notes", "Test fixture: list notes, optionally filtered by locked state.")]
public sealed class ListNotes : IQuery<ListNotesInput, IReadOnlyList<NoteDto>>
{
    public async Task<OneOf<IReadOnlyList<NoteDto>>> Run(ListNotesInput input, ActionContext ctx)
    {
        var notes = await ctx.Db.Set<Note>().AsNoTracking().ToListAsync(ctx.CancellationToken);
        return notes
            .Where(n => input.Locked is not { } locked || n.Locked == locked)
            .OrderBy(n => n.Text.Value, StringComparer.Ordinal)
            .Select(NoteDto.From)
            .ToList();
    }
}
