using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace AppTemplate.Architecture.Tests;

/// <summary>
/// Keeps the repo-owned skills in <c>.claude/skills/</c> from going stale. Every skill needs frontmatter whose
/// <c>name</c> equals its folder and whose <c>description</c> starts with "Use when". Every repo path, script,
/// <c>dotnet --project/--solution/-p</c> target and <c>npm run</c> script named in backticks must exist, and every
/// <c>`skill:name`</c> reference must name an installed skill.
/// </summary>
/// <remarks>
/// <para>Vendored skills (the official <c>shadcn</c> skill) are third-party content and are not linted.</para>
/// <para>
/// Tokens holding a placeholder (<c>&lt;</c>, <c>&gt;</c> or <c>*</c>) are skipped, as are plugin skills
/// (<c>dotnet-claude-kit:…</c>, <c>aspire:…</c>), which live outside the repo.
/// </para>
/// <para>
/// The skills use the Projects example as their worked example. <c>skill:remove-example</c> deletes it, so its paths
/// are checked only while <c>src/AppTemplate.Domain/Projects</c> exists.
/// </para>
/// </remarks>
public sealed partial class SkillLintTests
{
    private static readonly string[] ExpectedSkills =
    [
        "domain-modeling", "ef-core", "tdd", "add-action", "add-aggregate", "change-action-contract",
        "frontend-feature", "investigate", "remove-example", "rename-template", "swap-to-postgres",
    ];

    private static readonly HashSet<string> VendoredSkills = new(StringComparer.Ordinal) { "shadcn" };

    private static readonly string[] PathPrefixes = ["src/", "tests/", "scripts/", "docs/", ".claude/"];

    private static readonly HashSet<string> TargetFlags = new(StringComparer.Ordinal) { "--project", "--solution", "-p" };

    private const string ExampleMarker = "src/AppTemplate.Domain/Projects";

    private static readonly string[] ExamplePaths =
    [
        "src/AppTemplate.Domain/Projects",
        "src/AppTemplate.Api/Features/Projects",
        "src/AppTemplate.Api/Data/ProjectConfiguration.cs",
        "tests/AppTemplate.Api.Tests/Features/Projects",
        "tests/AppTemplate.Api.Tests/Domain/Project",
        "tests/AppTemplate.Api.Tests/Domain/TaskTitle",
        "src/web/src/pages/project",
    ];

    private static readonly string Root = FindRepoRoot();

    private static readonly IReadOnlyList<Skill> RepoSkills = LoadRepoSkills();

    [Fact]
    public void RepoSkills_AllExpectedPresent()
    {
        var names = RepoSkills.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        var missing = ExpectedSkills.Where(e => !names.Contains(e)).ToList();

        missing.ShouldBeEmpty($"Missing repo skills (.claude/skills/<name>/SKILL.md): {string.Join(", ", missing)}");
        names.Count.ShouldBeGreaterThanOrEqualTo(ExpectedSkills.Length);
    }

    [Fact]
    public void RepoSkills_HaveValidFrontmatter()
    {
        RepoSkills.ShouldNotBeEmpty();

        var problems = new List<string>();
        foreach (var skill in RepoSkills)
        {
            var frontmatter = ParseFrontmatter(skill.Text);
            if (frontmatter is null)
            {
                problems.Add($"{skill.Name}: SKILL.md must start with a '---' frontmatter block closed by '---'");
                continue;
            }

            if (!frontmatter.TryGetValue("name", out var name) || name != skill.Name)
            {
                problems.Add($"{skill.Name}: frontmatter name must equal the folder name '{skill.Name}' (found '{name}')");
            }

            if (!frontmatter.TryGetValue("description", out var description)
                || !description.StartsWith("Use when", StringComparison.Ordinal))
            {
                problems.Add($"{skill.Name}: frontmatter description must start with \"Use when\" (found '{description}')");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void RepoSkills_ReferencedPathsExist()
    {
        RepoSkills.ShouldNotBeEmpty();
        var exampleRemoved = !Directory.Exists(Path.Combine(Root, ExampleMarker));

        var checkedCount = 0;
        var missing = new List<string>();
        foreach (var skill in RepoSkills)
        {
            foreach (var path in ReferencedPaths(skill))
            {
                if (exampleRemoved && ExamplePaths.Any(e => path.StartsWith(e, StringComparison.Ordinal)))
                {
                    continue;
                }

                checkedCount++;
                var full = Path.Combine(Root, path);
                if (!File.Exists(full) && !Directory.Exists(full))
                {
                    missing.Add($"{skill.Name}: {path}");
                }
            }
        }

        checkedCount.ShouldBeGreaterThan(0);
        missing.ShouldBeEmpty($"Skills name paths that do not exist:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    public void RepoSkills_ReferencedNpmScriptsExist()
    {
        RepoSkills.ShouldNotBeEmpty();
        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "web", "package.json")));
        var scripts = package.RootElement.GetProperty("scripts").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        scripts.ShouldNotBeEmpty();

        var referenced = RepoSkills
            .SelectMany(s => CodeSegments(s.Text).SelectMany(c => NpmRun().Matches(c).Select(m => (s.Name, Script: m.Groups[1].Value))))
            .ToList();
        referenced.ShouldNotBeEmpty();

        var missing = referenced.Where(r => !scripts.Contains(r.Script)).Select(r => $"{r.Name}: npm run {r.Script}").Distinct().ToList();

        missing.ShouldBeEmpty($"Skills name npm scripts missing from src/web/package.json:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    public void RepoSkills_ReferencedSkillsExist()
    {
        RepoSkills.ShouldNotBeEmpty();
        var installed = Directory.GetDirectories(Path.Combine(Root, ".claude", "skills"))
            .Where(d => File.Exists(Path.Combine(d, "SKILL.md")))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);

        var referenced = RepoSkills
            .SelectMany(s => SkillReference().Matches(s.Text).Select(m => (s.Name, Target: m.Groups[1].Value)))
            .ToList();
        referenced.ShouldNotBeEmpty();

        var missing = referenced.Where(r => !installed.Contains(r.Target)).Select(r => $"{r.Name}: skill:{r.Target}").Distinct().ToList();

        missing.ShouldBeEmpty($"Skills reference skills that do not exist in .claude/skills:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    private static IEnumerable<string> ReferencedPaths(Skill skill)
    {
        foreach (var segment in CodeSegments(skill.Text))
        {
            var tokens = segment.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(Clean).ToList();
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Length == 0 || token.IndexOfAny(['<', '>', '*']) >= 0)
                {
                    continue;
                }

                var isTarget = i > 0 && TargetFlags.Contains(tokens[i - 1]);
                if (isTarget || PathPrefixes.Any(p => token.StartsWith(p, StringComparison.Ordinal)))
                {
                    yield return token;
                }
            }
        }
    }

    /// <summary>Every line of a fenced block, plus every inline code span outside fenced blocks.</summary>
    private static IEnumerable<string> CodeSegments(string markdown)
    {
        var inFence = false;
        foreach (var line in markdown.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                yield return line;
                continue;
            }

            foreach (Match span in InlineCode().Matches(line))
            {
                yield return span.Groups[1].Value;
            }
        }
    }

    /// <summary>Strips quotes, brackets and trailing sentence punctuation; keeps a leading dot (<c>.claude/</c>) and a trailing slash.</summary>
    private static string Clean(string token) => token.Trim('"', '\'', '(', ')', ',', ';', '`', '\r').TrimEnd('.', ':');

    private static Dictionary<string, string>? ParseFrontmatter(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0] != "---")
        {
            return null;
        }

        var end = Array.IndexOf(lines, "---", 1);
        if (end < 0)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines[1..end])
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && !char.IsWhiteSpace(line[0]))
            {
                values[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"', '\'');
            }
        }

        return values;
    }

    private static List<Skill> LoadRepoSkills()
    {
        var dir = Path.Combine(Root, ".claude", "skills");
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return
        [
            .. Directory.GetDirectories(dir)
                .Select(d => (Name: Path.GetFileName(d), File: Path.Combine(d, "SKILL.md")))
                .Where(s => !VendoredSkills.Contains(s.Name) && File.Exists(s.File))
                .Select(s => new Skill(s.Name, File.ReadAllText(s.File)))
                .OrderBy(s => s.Name, StringComparer.Ordinal),
        ];
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AppTemplate.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("AppTemplate.slnx not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex("`([^`\n]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"npm run ([A-Za-z0-9:_-]+)")]
    private static partial Regex NpmRun();

    [GeneratedRegex("`skill:([a-z][a-z0-9-]*)`")]
    private static partial Regex SkillReference();

    private sealed record Skill(string Name, string Text);
}
