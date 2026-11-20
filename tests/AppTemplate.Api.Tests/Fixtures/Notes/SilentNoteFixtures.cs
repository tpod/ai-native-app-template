using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

/// <summary>Deliberately invalid: an event without [Event]. The pipeline must refuse to commit it.</summary>
public sealed record UnnamedNoteEvent(Guid NoteId) : IDomainEvent;

public sealed record SilentNoteInput(Guid NoteId, string Text);

/// <summary>
/// Deliberately broken mutations for the history rules. Not part of <see cref="NoteFixture.Register"/>: tests that need
/// them add them to <see cref="ActionRegistryOptions.Types"/> themselves.
/// </summary>
public static class SilentNoteFixtures
{
    public static readonly Type[] Types = [typeof(TouchNoteSilently), typeof(EditNoteUnnamed)];
}

[Action("fixture_touch_note_silently", "Test fixture: change a note's text without raising an event (a handler bug).")]
public sealed class TouchNoteSilently : IMutation<SilentNoteInput, Note>
{
    public async Task<OneOf<Note, NoteNotFound>> Load(SilentNoteInput input, ActionContext ctx)
        => await NoteFixture.FindNote(input.NoteId, ctx) is { } note ? note : new NoteNotFound(NoteId.From(input.NoteId));

    public Task<OneOf<Success>> Apply(Note target, SilentNoteInput input, ActionContext ctx)
    {
        target.ReplaceTextSilently(NoteText.Create(input.Text).AsT0);
        return Task.FromResult<OneOf<Success>>(new Success());
    }

    public string Summarize(Note target, SilentNoteInput input) => $"Touch note '{target.Text.Value}'";

    public object ToResult(Note target) => NoteDto.From(target);
}

[Action("fixture_edit_note_unnamed", "Test fixture: change a note's text and raise an event without [Event] (a domain bug).")]
public sealed class EditNoteUnnamed : IMutation<SilentNoteInput, Note>
{
    public async Task<OneOf<Note, NoteNotFound>> Load(SilentNoteInput input, ActionContext ctx)
        => await NoteFixture.FindNote(input.NoteId, ctx) is { } note ? note : new NoteNotFound(NoteId.From(input.NoteId));

    public Task<OneOf<Success>> Apply(Note target, SilentNoteInput input, ActionContext ctx)
    {
        target.ReplaceTextWithUnnamedEvent(NoteText.Create(input.Text).AsT0);
        return Task.FromResult<OneOf<Success>>(new Success());
    }

    public string Summarize(Note target, SilentNoteInput input) => $"Edit note '{target.Text.Value}'";

    public object ToResult(Note target) => NoteDto.From(target);
}
