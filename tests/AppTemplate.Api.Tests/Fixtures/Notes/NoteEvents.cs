using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

[Event("note.created", 1)]
public sealed record NoteCreated(Guid NoteId, string Text) : IDomainEvent;

[Event("note.edited", 1)]
public sealed record NoteEdited(Guid NoteId, string OldText, string NewText) : IDomainEvent;

[Event("note.locked", 1)]
public sealed record NoteLockApplied(Guid NoteId) : IDomainEvent;
