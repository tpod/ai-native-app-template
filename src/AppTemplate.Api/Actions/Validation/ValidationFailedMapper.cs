using System.Text;
using System.Text.Json;
using AppTemplate.Domain.Common;
using FluentValidation.Results;

namespace AppTemplate.Api.Actions.Validation;

public static class ValidationFailedMapper
{
    public static ValidationFailed From(ValidationResult result)
        => new([.. result.Errors.Select(ToField)]);

    private static FieldFailure ToField(ValidationFailure failure)
    {
        var field = CamelCasePath(failure.PropertyName);
        var suggestion = failure.CustomState is SuggestionState state
            ? state.Suggestion
            : $"Provide a valid value for {field}.";
        return new FieldFailure(field, CodeFor(failure), failure.ErrorMessage, suggestion);
    }

    private static string CodeFor(ValidationFailure failure)
    {
        if (failure.ErrorCode is { } explicitCode && explicitCode.Contains('.', StringComparison.Ordinal))
        {
            return explicitCode;
        }

        // Code regex forbids indexers and dots, so use the leaf property name only.
        var leaf = failure.PropertyName[(failure.PropertyName.LastIndexOf('.') + 1)..];
        var bracket = leaf.IndexOf('[', StringComparison.Ordinal);
        if (bracket >= 0)
        {
            leaf = leaf[..bracket];
        }

        var validator = failure.ErrorCode ?? "invalid";
        if (validator.EndsWith("Validator", StringComparison.Ordinal) && validator.Length > "Validator".Length)
        {
            validator = validator[..^"Validator".Length];
        }

        return $"{ToSnakeCase(leaf)}.{ToSnakeCase(validator)}";
    }

    private static string CamelCasePath(string path)
        => string.Join('.', path.Split('.').Select(segment =>
        {
            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            var name = bracket >= 0 ? segment[..bracket] : segment;
            var indexer = bracket >= 0 ? segment[bracket..] : string.Empty;
            return JsonNamingPolicy.CamelCase.ConvertName(name) + indexer;
        }));

    /// <summary>ProjectId -> project_id, NotEmpty -> not_empty, URLPath -> url_path.</summary>
    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                var prevLower = !char.IsUpper(name[i - 1]);
                var nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (prevLower || nextLower)
                {
                    sb.Append('_');
                }
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
