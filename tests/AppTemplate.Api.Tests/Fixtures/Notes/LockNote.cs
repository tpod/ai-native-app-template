using AppTemplate.Api.Actions;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed record LockNoteInput(Guid NoteId);

[Action("fixture_lock_note", "Test fixture: lock a note so it can no longer be edited.", Destructive = true)]
public sealed class LockNote : IMutation<LockNoteInput, Note>
{
    public async Task<OneOf<Note, NoteNotFound>> Load(LockNoteInput input, ActionContext ctx)
        => await NoteFixture.FindNote(input.NoteId, ctx) is { } note ? note : new NoteNotFound(NoteId.From(input.NoteId));

    public Task<OneOf<Success, NoteLocked>> Apply(Note target, LockNoteInput input, ActionContext ctx)
        => Task.FromResult(target.Lock());

    public string Summarize(Note target, LockNoteInput input) => $"Lock note '{target.Text.Value}'";

    public object ToResult(Note target) => NoteDto.From(target);
}
