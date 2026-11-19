using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public readonly record struct NoteId : IValueObject<NoteId, Guid>
{
    private NoteId(Guid value) => Value = value;

    public Guid Value { get; }

    public static NoteId From(Guid value) => new(value);

    public static NoteId FromTrusted(Guid value) => new(value);
}
