using AppTemplate.Api.Actions;
using Microsoft.EntityFrameworkCore;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed record UntrackedEditNoteInput(Guid NoteId, string Text);

/// <summary>Deliberately broken: Load returns an untracked note, which the pipeline must reject, not silently skip.</summary>
[Action("fixture_untracked_edit", "Test fixture: edit a note loaded with AsNoTracking (a handler bug).")]
public sealed class UntrackedEditNote : IMutation<UntrackedEditNoteInput, Note>
{
    public async Task<OneOf<Note, NoteNotFound>> Load(UntrackedEditNoteInput input, ActionContext ctx)
    {
        var id = NoteId.From(input.NoteId);
        var note = await ctx.Db.Set<Note>().AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ctx.CancellationToken);
        return note is null ? new NoteNotFound(id) : note;
    }

    public Task<OneOf<Success, NoteLocked>> Apply(Note target, UntrackedEditNoteInput input, ActionContext ctx)
        => Task.FromResult(target.Edit(NoteText.Create(input.Text).AsT0));

    public string Summarize(Note target, UntrackedEditNoteInput input) => $"Edit note '{target.Text.Value}' -> '{input.Text}'";

    public object ToResult(Note target) => NoteDto.From(target);
}
