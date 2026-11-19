using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed record CreateNoteInput(string Text);

[Action("fixture_create_note", "Test fixture: create a note with the given text.")]
public sealed class CreateNote : IMutation<CreateNoteInput, Note>
{
    public Task<OneOf<Note, ValidationFailed>> Load(CreateNoteInput input, ActionContext ctx)
    {
        if (NoteText.Create(input.Text).TryPickT1(out var error, out var text))
        {
            return Task.FromResult<OneOf<Note, ValidationFailed>>(ValidationFailed.For("text", error));
        }

        var note = Note.Create(NoteId.From(ctx.NewId()), text);
        ctx.Db.Add(note);
        return Task.FromResult<OneOf<Note, ValidationFailed>>(note);
    }

    public Task<OneOf<Success>> Apply(Note target, CreateNoteInput input, ActionContext ctx)
        => Task.FromResult<OneOf<Success>>(new Success());

    public string Summarize(Note target, CreateNoteInput input) => $"Create note '{target.Text.Value}'";

    public object ToResult(Note target) => NoteDto.From(target);
}
