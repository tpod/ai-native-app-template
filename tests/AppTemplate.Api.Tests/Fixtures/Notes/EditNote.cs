using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed record EditNoteInput(Guid NoteId, string Text);

[Action("fixture_edit_note", "Test fixture: replace the text of a note. Warns note.unchanged when the text is the same.")]
public sealed class EditNote : IMutation<EditNoteInput, Note>
{
    public async Task<OneOf<Note, NoteNotFound>> Load(EditNoteInput input, ActionContext ctx)
        => await NoteFixture.FindNote(input.NoteId, ctx) is { } note ? note : new NoteNotFound(NoteId.From(input.NoteId));

    public Task<OneOf<Success, ValidationFailed, NoteLocked>> Apply(Note target, EditNoteInput input, ActionContext ctx)
    {
        if (NoteText.Create(input.Text).TryPickT1(out var error, out var text))
        {
            return Task.FromResult<OneOf<Success, ValidationFailed, NoteLocked>>(ValidationFailed.For("text", error));
        }

        if (text == target.Text)
        {
            ctx.Warn("note.unchanged", "The new text is the same as the current text; nothing will change.");
        }

        return Task.FromResult(target.Edit(text).Match<OneOf<Success, ValidationFailed, NoteLocked>>(s => s, locked => locked));
    }

    public string Summarize(Note target, EditNoteInput input) => $"Edit note '{target.Text.Value}' -> '{input.Text}'";

    public object ToResult(Note target) => NoteDto.From(target);
}
