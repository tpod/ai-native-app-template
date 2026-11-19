using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

[Error("note.not_found", ErrorKind.NotFound, "Note not found")]
public sealed record NoteNotFound(NoteId NoteId) : AppError
{
    public override string Message => $"Note {NoteId.Value} does not exist.";
    public override string Suggestion => "Check the id of the note.";
    public override object? Details => new { noteId = NoteId.Value };
}

[Error("note.locked", ErrorKind.Conflict, "Note is locked")]
public sealed record NoteLocked(NoteId NoteId) : AppError
{
    public override string Message => $"Note {NoteId.Value} is locked and cannot be changed.";
    public override string Suggestion => "Locked notes are read-only; create a new note instead.";
    public override object? Details => new { noteId = NoteId.Value };
}
