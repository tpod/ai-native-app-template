using AppTemplate.Api.Actions.Validation;
using FluentValidation;

namespace AppTemplate.Api.Features.Outbox;

public sealed class ListOutboxDeliveriesInputValidator : AbstractValidator<ListOutboxDeliveriesInput>
{
    public ListOutboxDeliveriesInputValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, ListOutboxDeliveries.MaxLimit)
            .When(x => x.Limit is not null)
            .WithErrorCode("outbox.limit_out_of_range")
            .WithSuggestion($"Use a limit from 1 to {ListOutboxDeliveries.MaxLimit}, or omit it for {ListOutboxDeliveries.DefaultLimit}.");
    }
}
