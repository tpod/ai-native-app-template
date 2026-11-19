using AppTemplate.Api.Actions;
using Microsoft.EntityFrameworkCore;
using OneOf;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed record GetNoteInput(Guid NoteId);

public sealed record NoteDto(Guid Id, string Text, bool Locked, int Version)
{
    public static NoteDto From(Note note) => new(note.Id.Value, note.Text.Value, note.Locked, note.Version);
}

[Query("fixture_get_note", "Test fixture: get one note by id.")]
public sealed class GetNote : IQuery<GetNoteInput, NoteDto>
{
    public async Task<OneOf<NoteDto, NoteNotFound>> Run(GetNoteInput input, ActionContext ctx)
    {
        var id = NoteId.From(input.NoteId);
        var note = await ctx.Db.Set<Note>().AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ctx.CancellationToken);
        return note is null ? new NoteNotFound(id) : NoteDto.From(note);
    }
}
