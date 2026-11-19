using AppTemplate.Domain.Common;
using FluentValidation;
using FluentValidation.Results;
using OneOf;

namespace AppTemplate.Api.Actions.Validation;

/// <summary>Carried in <see cref="ValidationFailure.CustomState"/> to give a failure its own fix suggestion.</summary>
public sealed record SuggestionState(string Suggestion);

public static class ValidationRuleExtensions
{
    /// <summary>
    /// Runs a value-object factory and records its <see cref="ValidationError"/> as a failure (no exception).
    /// The failure carries the error's code, message and suggestion.
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, TProp> MustBeValid<T, TProp, TVo>(
        this IRuleBuilder<T, TProp> rule,
        Func<TProp, OneOf<TVo, ValidationError>> create)
        => rule.Custom((value, context) =>
        {
            if (create(value).TryPickT1(out var error, out _))
            {
                context.AddFailure(new ValidationFailure(context.PropertyPath, error.Message)
                {
                    ErrorCode = error.Code,
                    CustomState = new SuggestionState(error.Suggestion),
                });
            }
        });

    /// <summary>Sets the suggestion surfaced for failures of this rule.</summary>
    public static IRuleBuilderOptions<T, TProp> WithSuggestion<T, TProp>(
        this IRuleBuilderOptions<T, TProp> rule,
        string suggestion)
        => rule.Configure(r => r.Current.CustomStateProvider = (_, _) => new SuggestionState(suggestion));
}
