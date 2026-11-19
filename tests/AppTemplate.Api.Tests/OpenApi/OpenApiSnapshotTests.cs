using System.Text.Json;
using System.Text.Json.Nodes;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.OpenApi;

/// <summary>The committed <c>src/web/openapi.json</c> is the contract the frontend client is generated from.</summary>
public sealed class OpenApiSnapshotTests : ApiTestBase
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // No fixtures: the snapshot describes only the real app's actions.
    protected override ApiFactory CreateFactory() => new(includeFixtures: false);

    [Fact]
    public async Task OpenApi_Live_MatchesCommittedSnapshot()
    {
        var live = Normalize(await OpenApiDocument.Fetch(Client));
        var path = Path.Combine(RepoRoot(), "src", "web", "openapi.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, live, TestContext.Current.CancellationToken);
            return;
        }

        File.Exists(path).ShouldBeTrue("OpenAPI drift: run scripts/update-openapi.sh");
        var committed = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal);
        committed.ShouldBe(live, "OpenAPI drift: run scripts/update-openapi.sh");
    }

    [Fact(Skip = "Task 13")]
    public async Task OpenApi_ExecuteOperation_RequiresPreviewToken()
    {
        var doc = await OpenApiDocument.Fetch(Client);
        var operation = doc["paths"]!["/api/actions/rename_project"]!["post"]!;
        var allOf = operation["requestBody"]!["content"]!["application/json"]!["schema"]!["allOf"]!.AsArray();
        var tokenSchema = allOf.Single(s => s!["properties"]?["previewToken"] is not null)!;
        tokenSchema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldContain("previewToken");
    }

    internal static string Normalize(JsonNode node) => Sort(node)!.ToJsonString(Indented) + "\n";

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sort).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };

    private static string RepoRoot()
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
}

/// <summary>Uses the Note fixture actions, which exist only in the test host.</summary>
public sealed class OpenApiProblemTests : ApiTestBase
{
    [Fact]
    public async Task OpenApi_ErrorResponses_UseProblemJson()
    {
        var doc = await OpenApiDocument.Fetch(Client);
        var checkedOperations = 0;
        var checkedResponses = 0;

        foreach (var (route, item) in doc["paths"]!.AsObject())
        {
            if (!route.StartsWith("/api/actions/fixture_", StringComparison.Ordinal))
            {
                continue;
            }

            var operation = item!["post"]!;
            var operationId = operation["operationId"]!.GetValue<string>();
            operationId.ShouldBe(route.EndsWith("/preview", StringComparison.Ordinal)
                ? "preview_" + route["/api/actions/".Length..^"/preview".Length]
                : route["/api/actions/".Length..]);
            checkedOperations++;

            foreach (var (status, response) in operation["responses"]!.AsObject())
            {
                if (status.StartsWith('2'))
                {
                    continue;
                }

                var content = response!["content"]!.AsObject();
                content.ContainsKey("application/problem+json").ShouldBeTrue($"{operationId} {status}");
                checkedResponses++;
            }
        }

        var execute = doc["paths"]!["/api/actions/fixture_create_note"]!["post"]!;
        var schema = execute["requestBody"]!["content"]!["application/json"]!["schema"]!["allOf"]!.AsArray();
        schema.Any(s => s!["required"]?.AsArray().Any(r => r!.GetValue<string>() == "previewToken") == true).ShouldBeTrue();

        checkedOperations.ShouldBeGreaterThan(0);
        checkedResponses.ShouldBeGreaterThan(0);

        var problem = doc["components"]!["schemas"]!["ProblemDetails"]!["properties"]!.AsObject();
        foreach (var member in new[] { "code", "details", "suggestion", "traceId" })
        {
            problem.ContainsKey(member).ShouldBeTrue(member);
        }
    }
}

internal static class OpenApiDocument
{
    public static async Task<JsonNode> Fetch(HttpClient client)
    {
        var text = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        return JsonNode.Parse(text) ?? throw new InvalidOperationException("Empty OpenAPI document.");
    }
}
