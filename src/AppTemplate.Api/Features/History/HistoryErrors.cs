using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Features.History;

[Error("history.unknown_aggregate_type", ErrorKind.BadRequest, "Unknown aggregate type")]
public sealed record UnknownAggregateType(string Given, IReadOnlyList<string> Known) : AppError
{
    public override string Message => $"There is no aggregate type named '{Given}'.";
    public override string Suggestion => $"Use one of: {string.Join(", ", Known)}.";
    public override object? Details => new { given = Given, known = Known };
}
