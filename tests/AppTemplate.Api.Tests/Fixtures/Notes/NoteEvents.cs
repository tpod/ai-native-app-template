using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed record NoteCreated(Guid NoteId, string Text) : IDomainEvent;

public sealed record NoteEdited(Guid NoteId, string OldText, string NewText) : IDomainEvent;
