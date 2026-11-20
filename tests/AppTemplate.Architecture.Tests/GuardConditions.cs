using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;

namespace AppTemplate.Architecture.Tests;

/// <summary>
/// Conditions for the guards that need something to check. Each guard asserts its collection is non-empty, so with no
/// aggregate or no action (after <c>skill:remove-example</c>) it would fail; these conditions skip it instead and
/// re-enable it automatically as soon as the first aggregate or action exists.
/// </summary>
/// <remarks>
/// Both are plain reflection scans, independent of the code under test: the action condition never builds the registry,
/// so a broken registry still fails the guards' non-empty asserts.
/// </remarks>
public static class GuardConditions
{
    public const string NoAggregateReason = "No aggregate in the Domain yet; re-enables automatically when one exists.";

    public const string NoActionReason = "No [Action] or [Query] in the Api yet; re-enables automatically when one exists.";

    /// <summary>True when the Domain assembly contains a concrete subclass of <see cref="AggregateRoot{TId}"/>.</summary>
    public static bool DomainHasAggregate { get; } =
        typeof(Entity<>).Assembly.GetTypes().Any(t => t is { IsClass: true, IsAbstract: false } && DerivesFrom(t, typeof(AggregateRoot<>)));

    /// <summary>True when the Api assembly contains a type marked <see cref="ActionAttribute"/> or <see cref="QueryAttribute"/>.</summary>
    public static bool ApiHasActions { get; } =
        typeof(ActionRegistryOptions).Assembly.GetTypes().Any(t =>
            t.IsDefined(typeof(ActionAttribute), inherit: false) || t.IsDefined(typeof(QueryAttribute), inherit: false));

    private static bool DerivesFrom(Type type, Type openGeneric)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (b.IsGenericType && b.GetGenericTypeDefinition() == openGeneric)
            {
                return true;
            }
        }

        return false;
    }
}
