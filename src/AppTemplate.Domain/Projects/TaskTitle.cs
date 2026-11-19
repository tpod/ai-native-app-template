using AppTemplate.Domain.Common;
using OneOf;

namespace AppTemplate.Domain.Projects;

public sealed record TaskTitle : IValueObject<TaskTitle, string>
{
    public const int MaxLength = 200;

    private TaskTitle(string value) => Value = value;

    public string Value { get; }

    public static OneOf<TaskTitle, ValidationError> Create(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new ValidationError("task_title.empty", "Task title is empty.", "Provide a non-blank task title.");
        }

        if (trimmed.Length > MaxLength)
        {
            return new ValidationError(
                "task_title.too_long",
                $"Task title is {trimmed.Length} characters; the maximum is {MaxLength}.",
                $"Shorten the title to at most {MaxLength} characters.");
        }

        return new TaskTitle(trimmed);
    }

    public static TaskTitle FromTrusted(string value) => new(value);
}
