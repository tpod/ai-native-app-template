using AppTemplate.Domain.Common;
using OneOf;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed record NoteText : IValueObject<NoteText, string>
{
    public const int MaxLength = 50;

    private NoteText(string value) => Value = value;

    public string Value { get; }

    public static OneOf<NoteText, ValidationError> Create(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new ValidationError("note_text.empty", "Note text is empty.", "Provide non-blank note text.");
        }

        if (trimmed.Length > MaxLength)
        {
            return new ValidationError(
                "note_text.too_long",
                $"Note text is {trimmed.Length} characters; the maximum is {MaxLength}.",
                $"Shorten the text to at most {MaxLength} characters.");
        }

        return new NoteText(trimmed);
    }

    public static NoteText FromTrusted(string value) => new(value);
}
