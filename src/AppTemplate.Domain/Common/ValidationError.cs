namespace AppTemplate.Domain.Common;

/// <summary>Field-level error returned by value-object <c>Create</c>. Not an <see cref="AppError"/>.</summary>
public sealed record ValidationError(string Code, string Message, string Suggestion);
