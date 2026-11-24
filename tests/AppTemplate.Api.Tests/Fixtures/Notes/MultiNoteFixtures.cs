using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed record EditTwoNotesInput(Guid NoteId, Guid OtherNoteId, string Text);

/// <summary>
/// Mutations that change two aggregates in one execute. Not part of <see cref="NoteFixture.Register"/>: tests that
/// need them add them to <see cref="ActionRegistryOptions.Types"/> themselves.
/// </summary>
public static class MultiNoteFixtures
{
    public static readonly Type[] Types = [typeof(EditTwoNotes), typeof(EditNoteTouchOther)];

    internal static async Task<OneOf<Note, NoteNotFound>> LoadBoth(EditTwoNotesInput input, ActionContext ctx)
    {
        if (await NoteFixture.FindNote(input.OtherNoteId, ctx) is null)
        {
            return new NoteNotFound(NoteId.From(input.OtherNoteId));
        }

        return await NoteFixture.FindNote(input.NoteId, ctx) is { } note ? note : new NoteNotFound(NoteId.From(input.NoteId));
    }
}

[Action("fixture_edit_two_notes", "Test fixture: set the same text on two notes in one execute.")]
public sealed class EditTwoNotes : IMutation<EditTwoNotesInput, Note>
{
    public Task<OneOf<Note, NoteNotFound>> Load(EditTwoNotesInput input, ActionContext ctx) => MultiNoteFixtures.LoadBoth(input, ctx);

    public async Task<OneOf<Success, NoteLocked>> Apply(Note target, EditTwoNotesInput input, ActionContext ctx)
    {
        var text = NoteText.Create(input.Text).AsT0;
        var other = (await NoteFixture.FindNote(input.OtherNoteId, ctx))!;
        var first = target.Edit(text);
        if (first.IsT1)
        {
            return first.AsT1;
        }

        return other.Edit(text).Match<OneOf<Success, NoteLocked>>(s => s, locked => locked);
    }

    public string Summarize(Note target, EditTwoNotesInput input) => $"Edit two notes to '{input.Text}'";

    public object ToResult(Note target) => NoteDto.From(target);
}

[Action("fixture_edit_note_touch_other", "Test fixture: edit one note and change a second note without an event (a handler bug).")]
public sealed class EditNoteTouchOther : IMutation<EditTwoNotesInput, Note>
{
    public Task<OneOf<Note, NoteNotFound>> Load(EditTwoNotesInput input, ActionContext ctx) => MultiNoteFixtures.LoadBoth(input, ctx);

    public async Task<OneOf<Success, NoteLocked>> Apply(Note target, EditTwoNotesInput input, ActionContext ctx)
    {
        var text = NoteText.Create(input.Text).AsT0;
        (await NoteFixture.FindNote(input.OtherNoteId, ctx))!.ReplaceTextSilently(text);
        return target.Edit(text).Match<OneOf<Success, NoteLocked>>(s => s, locked => locked);
    }

    public string Summarize(Note target, EditTwoNotesInput input) => $"Edit note to '{input.Text}' and touch another";

    public object ToResult(Note target) => NoteDto.From(target);
}
