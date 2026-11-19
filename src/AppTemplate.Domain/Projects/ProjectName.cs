using AppTemplate.Domain.Common;
using OneOf;

namespace AppTemplate.Domain.Projects;

public sealed record ProjectName : IValueObject<ProjectName, string>
{
    public const int MaxLength = 100;

    private ProjectName(string value) => Value = value;

    public string Value { get; }

    public static OneOf<ProjectName, ValidationError> Create(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new ValidationError("project_name.empty", "Project name is empty.", "Provide a non-blank project name.");
        }

        if (trimmed.Length > MaxLength)
        {
            return new ValidationError(
                "project_name.too_long",
                $"Project name is {trimmed.Length} characters; the maximum is {MaxLength}.",
                $"Shorten the name to at most {MaxLength} characters.");
        }

        return new ProjectName(trimmed);
    }

    public static ProjectName FromTrusted(string value) => new(value);
}
