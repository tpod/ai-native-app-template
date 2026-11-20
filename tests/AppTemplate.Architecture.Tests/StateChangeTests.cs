using AppTemplate.Api.Actions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace AppTemplate.Architecture.Tests;

/// <summary>
/// Golden rule 10: every state change goes through an action. Only <see cref="ActionPipeline"/> may save, so history,
/// validation, stale checks and telemetry stay complete. A new project with state-changing code must be added here.
/// </summary>
public sealed class StateChangeTests
{
    private static readonly string[] Assemblies = [typeof(ActionRegistryOptions).Assembly.Location];

    [Fact]
    public void StateChanges_GoThroughActionPipeline()
    {
        var hits = Assemblies.SelectMany(StateChangingCalls).ToList();

        var allowed = hits.Where(h => h.Owner == nameof(ActionPipeline)).ToList();
        allowed.Count.ShouldBe(1, $"Expected exactly one save in ActionPipeline, found: {string.Join(", ", allowed.Select(a => a.Call))}");
        allowed[0].Call.ShouldBe("SaveChangesAsync");

        var offenders = hits.Except(allowed).Select(h => $"{h.Method} calls {h.Call}").ToList();
        offenders.ShouldBeEmpty(
            "Only ActionPipeline may change state. Call an action (ActionRunner for in-process code) instead:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Every call to a state-changing EF API in the assembly, outside migrations and the design-time factory.</summary>
    internal static IEnumerable<(string Owner, string Method, string Call)> StateChangingCalls(string assemblyPath)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);
        foreach (var type in module.GetTypes())
        {
            var top = type;
            while (top.DeclaringType is not null)
            {
                top = top.DeclaringType;
            }

            if (top.Namespace.EndsWith(".Data.Migrations", StringComparison.Ordinal) || top.Name == "AppDbContextDesignTimeFactory")
            {
                continue;
            }

            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode.Code is Code.Call or Code.Callvirt
                        && instruction.Operand is MethodReference target
                        && IsStateChanging(target))
                    {
                        yield return (top.Name, $"{type.FullName}.{method.Name}", target.Name);
                    }
                }
            }
        }
    }

    private static bool IsStateChanging(MethodReference target)
    {
        var name = target.Name;
        var declaring = target.DeclaringType.FullName;
        if (name is "SaveChanges" or "SaveChangesAsync")
        {
            return declaring.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) || declaring.StartsWith("AppTemplate.", StringComparison.Ordinal);
        }

        if (!declaring.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            return false;
        }

        return name.StartsWith("ExecuteUpdate", StringComparison.Ordinal)
            || name.StartsWith("ExecuteDelete", StringComparison.Ordinal)
            || name.StartsWith("ExecuteSql", StringComparison.Ordinal);
    }
}
