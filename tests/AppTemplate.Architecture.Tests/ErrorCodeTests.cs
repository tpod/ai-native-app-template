using System.Text.RegularExpressions;
using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;
using Shouldly;

namespace AppTemplate.Architecture.Tests;

public sealed partial class ErrorCodeTests
{
    // The spec deliberately returns request.not_found for an unknown HTTP path and for an unknown MCP tool.
    // Any other shared code is a mistake.
    private static readonly HashSet<string> SharedCodes = ["request.not_found"];

    [Fact]
    public void ErrorCodes_AreUniqueAndWellFormed()
    {
        var errors = new[] { typeof(AppError).Assembly, typeof(ActionRegistryOptions).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(AppError).IsAssignableFrom(t))
            .ToList();
        errors.ShouldNotBeEmpty();

        var problems = new List<string>();
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in errors)
        {
            var attribute = type.GetCustomAttributes(typeof(ErrorAttribute), inherit: false).Cast<ErrorAttribute>().SingleOrDefault();
            if (attribute is null)
            {
                problems.Add($"{type.FullName}: missing [Error(code, kind, title)]");
                continue;
            }

            if (!CodePattern().IsMatch(attribute.Code))
            {
                problems.Add($"{type.FullName}: code '{attribute.Code}' must be lowercase dotted, e.g. project.not_found");
            }

            if (string.IsNullOrWhiteSpace(attribute.Title))
            {
                problems.Add($"{type.FullName}: title must be non-empty");
            }

            if (!codes.TryAdd(attribute.Code, type.FullName!) && !SharedCodes.Contains(attribute.Code))
            {
                problems.Add($"{type.FullName}: code '{attribute.Code}' duplicates {codes[attribute.Code]}");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    /// <summary>The "Framework codes" line in AGENTS.md lists exactly the error codes the Api framework defines.</summary>
    [Fact]
    public void FrameworkErrorCodes_MatchAgentsMd()
    {
        var defined = typeof(ActionRegistryOptions).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(AppError).IsAssignableFrom(t) && t.Namespace?.Contains(".Features", StringComparison.Ordinal) != true)
            .Select(t => t.GetCustomAttributes(typeof(ErrorAttribute), inherit: false).Cast<ErrorAttribute>().Single().Code)
            .ToHashSet(StringComparer.Ordinal);
        defined.ShouldNotBeEmpty();

        var line = File.ReadLines(Path.Combine(SkillLintTests.FindRepoRoot(), "AGENTS.md"))
            .SingleOrDefault(l => l.StartsWith("- Framework codes:", StringComparison.Ordinal));
        line.ShouldNotBeNull("AGENTS.md must keep a '- Framework codes:' line under Errors.");
        var documented = BacktickedCode().Matches(line).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        documented.OrderBy(c => c, StringComparer.Ordinal).ShouldBe(defined.OrderBy(c => c, StringComparer.Ordinal));
    }

    [GeneratedRegex(@"^[a-z_]+\.[a-z_]+$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"`([a-z_]+\.[a-z_]+)`")]
    private static partial Regex BacktickedCode();
}
