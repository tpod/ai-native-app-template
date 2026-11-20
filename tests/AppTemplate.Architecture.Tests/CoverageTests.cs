using System.Reflection;
using AppTemplate.Api.Actions;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;
using Case = AppTemplate.Api.Tests.Infrastructure.Case;

namespace AppTemplate.Architecture.Tests;

public sealed class CoverageTests
{
    private static readonly Assembly TestAssembly = typeof(CoversAttribute).Assembly;

    private static ActionRegistry BuildRegistry()
    {
        var options = new ActionRegistryOptions();
        return ActionRegistry.Build(options.Assemblies.SelectMany(a => a.GetTypes()).Concat(options.Types), ArchitectureTests.HasValidator);
    }

    private static Type? TestsClassFor(ActionDescriptor action) =>
        TestAssembly.GetTypes().SingleOrDefault(t => t.Name == $"{action.HandlerType.Name}Tests");

    [Fact(Skip = GuardConditions.NoActionReason, SkipType = typeof(GuardConditions), SkipUnless = nameof(GuardConditions.ApiHasActions))]
    public void EveryAction_HasMatchingTestsClass()
    {
        var actions = BuildRegistry().All;
        actions.ShouldNotBeEmpty();

        var missing = actions.Where(a => TestsClassFor(a) is null).Select(a => $"{a.Name} (expected class {a.HandlerType.Name}Tests)").ToList();

        missing.ShouldBeEmpty($"Actions without a tests class:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact(Skip = GuardConditions.NoActionReason, SkipType = typeof(GuardConditions), SkipUnless = nameof(GuardConditions.ApiHasActions))]
    public void EveryAction_HasCoversForHappyPathEachErrorAndStale()
    {
        var actions = BuildRegistry().All;
        actions.ShouldNotBeEmpty();

        var missing = new List<string>();
        foreach (var action in actions)
        {
            var covers = (TestsClassFor(action)?.GetMethods() ?? [])
                .SelectMany(m => m.GetCustomAttributes<CoversAttribute>())
                .Where(c => c.Action == action.Name)
                .ToList();

            var required = new List<string> { nameof(Case.HappyPath) };
            if (action.Kind == ActionKind.Mutation)
            {
                required.Add(nameof(Case.Stale));
            }

            required.AddRange(action.ErrorCodes);

            var present = covers
                .Select(c => c.ErrorCode ?? c.Case!.Value.ToString())
                .ToHashSet(StringComparer.Ordinal);

            missing.AddRange(required.Where(r => !present.Contains(r)).Select(r => $"{action.Name} {r}"));
        }

        missing.ShouldBeEmpty($"Missing [Covers] (action, case/code):{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }
}
