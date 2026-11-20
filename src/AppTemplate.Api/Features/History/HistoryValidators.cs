using AppTemplate.Api.Actions.Validation;
using FluentValidation;

namespace AppTemplate.Api.Features.History;

public sealed class GetHistoryInputValidator : AbstractValidator<GetHistoryInput>
{
    public GetHistoryInputValidator()
    {
        RuleFor(x => x.AggregateType).NotEmpty()
            .WithErrorCode("history.field_required")
            .WithSuggestion("Pass the aggregate's type name, e.g. \"Project\".");
        RuleFor(x => x.AggregateId).NotEmpty()
            .WithErrorCode("history.field_required")
            .WithSuggestion("Pass the aggregate's id, e.g. a project id from list_projects.");
        RuleFor(x => x.Limit).InclusiveBetween(1, GetHistory.MaxLimit)
            .When(x => x.Limit is not null)
            .WithErrorCode("history.limit_out_of_range")
            .WithSuggestion($"Use a limit from 1 to {GetHistory.MaxLimit}, or omit it for {GetHistory.DefaultLimit}.");
    }
}
