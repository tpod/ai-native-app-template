using System.Reflection;
using System.Text.RegularExpressions;
using AppTemplate.Domain.Common;
using Shouldly;

namespace AppTemplate.Architecture.Tests;

/// <summary>
/// Event names are the history contract: a stored row keeps its name forever, so it must survive C# renames.
/// </summary>
public sealed partial class EventNameTests
{
    private static readonly Assembly Domain = typeof(Entity<>).Assembly;

    [Fact(Skip = GuardConditions.NoAggregateReason, SkipType = typeof(GuardConditions), SkipUnless = nameof(GuardConditions.DomainHasAggregate))]
    public void DomainEvents_AllHaveEventAttribute()
    {
        var events = DomainEvents();
        events.ShouldNotBeEmpty();

        var missing = events.Where(t => t.GetCustomAttribute<EventAttribute>(inherit: false) is null)
            .Select(t => $"{t.FullName}: missing [Event(name, version)]")
            .ToList();

        missing.ShouldBeEmpty(string.Join(Environment.NewLine, missing));
    }

    [Fact(Skip = GuardConditions.NoAggregateReason, SkipType = typeof(GuardConditions), SkipUnless = nameof(GuardConditions.DomainHasAggregate))]
    public void EventNames_AreUniqueAndWellFormed()
    {
        var events = DomainEvents();
        events.ShouldNotBeEmpty();

        var problems = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in events)
        {
            if (type.GetCustomAttribute<EventAttribute>(inherit: false) is not { } attribute)
            {
                continue;
            }

            if (!NamePattern().IsMatch(attribute.Name))
            {
                problems.Add($"{type.FullName}: name '{attribute.Name}' must be lowercase dotted, e.g. project.renamed");
            }

            if (attribute.Version < 1)
            {
                problems.Add($"{type.FullName}: version {attribute.Version} must be 1 or higher");
            }

            if (!names.TryAdd(attribute.Name, type.FullName!))
            {
                problems.Add($"{type.FullName}: name '{attribute.Name}' duplicates {names[attribute.Name]}");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    private static List<Type> DomainEvents()
        => [.. Domain.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IDomainEvent).IsAssignableFrom(t))];

    [GeneratedRegex("^[a-z_]+\\.[a-z_]+$")]
    private static partial Regex NamePattern();
}
