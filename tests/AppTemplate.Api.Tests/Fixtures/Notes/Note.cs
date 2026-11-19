using AppTemplate.Domain.Common;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed class Note : AggregateRoot<NoteId>
{
    // EF only.
    private Note() => Text = null!;

    private Note(NoteId id, NoteText text)
    {
        Id = id;
        Text = text;
    }

    public NoteText Text { get; private set; }

    public bool Locked { get; private set; }

    public static Note Create(NoteId id, NoteText text)
    {
        var note = new Note(id, text);
        note.Raise(new NoteCreated(id.Value, text.Value));
        return note;
    }

    public OneOf<Success, NoteLocked> Edit(NoteText text)
    {
        if (Locked)
        {
            return new NoteLocked(Id);
        }

        if (string.Equals(text.Value, Text.Value, StringComparison.Ordinal))
        {
            return new Success();
        }

        var old = Text;
        Text = text;
        Raise(new NoteEdited(Id.Value, old.Value, text.Value));
        return new Success();
    }

    public OneOf<Success, NoteLocked> Lock()
    {
        if (Locked)
        {
            return new NoteLocked(Id);
        }

        Locked = true;
        return new Success();
    }
}
