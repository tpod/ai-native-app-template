namespace AppTemplate.Domain.Common;

public sealed record FieldFailure(string Field, string Code, string Message, string Suggestion);

[Error("validation.failed", ErrorKind.Validation, "Validation failed")]
public sealed record ValidationFailed(IReadOnlyList<FieldFailure> Fields) : AppError
{
    public override string Message
        => $"{Fields.Count} field(s) failed validation: {string.Join(", ", Fields.Select(f => f.Field))}";

    public override string Suggestion
        => "Fix each field listed in details.fields (each has its own suggestion) and call the tool again.";

    public override object? Details => new { fields = Fields };

    public static ValidationFailed For(string field, ValidationError error)
        => new([new FieldFailure(field, error.Code, error.Message, error.Suggestion)]);
}
