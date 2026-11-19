using System.Text.Json;
using System.Text.Json.Nodes;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.OpenApi;

/// <summary>The committed <c>src/web/openapi.json</c> is the contract the frontend client is generated from.</summary>
public sealed class OpenApiSnapshotTests : ApiTestBase
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n" }; // LF on every OS

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
public sealed class OpenApiFrameworkErrorTests : ApiTestBase
{
    [Fact]
    public async Task OpenApi_Operations_DocumentFrameworkErrorStatusesWithCodes()
    {
        var doc = await OpenApiDocument.Fetch(Client);
        var operations = doc["paths"]!.AsObject()
            .Where(p => p.Key.StartsWith("/api/actions/fixture_", StringComparison.Ordinal) || p.Key.StartsWith("/api/queries/fixture_", StringComparison.Ordinal))
            .Select(p => (Route: p.Key, Op: p.Value!["post"]!))
            .ToList();
        operations.ShouldNotBeEmpty();

        var executes = 0;
        foreach (var (route, op) in operations)
        {
            var responses = op["responses"]!.AsObject();
            foreach (var status in new[] { "400", "500" })
            {
                responses.ContainsKey(status).ShouldBeTrue($"{route} {status}");
            }

            var isExecute = route.StartsWith("/api/actions/", StringComparison.Ordinal) && !route.EndsWith("/preview", StringComparison.Ordinal);
            if (isExecute)
            {
                executes++;
                responses.ContainsKey("409").ShouldBeTrue($"{route} 409");
                responses["409"]!["description"]!.GetValue<string>().ShouldContain("preview.stale");
                responses["400"]!["description"]!.GetValue<string>().ShouldContain("preview.token_invalid");
            }

            responses["400"]!["description"]!.GetValue<string>().ShouldContain("request.malformed");
            responses["500"]!["description"]!.GetValue<string>().ShouldContain("internal.unexpected");

            var problems = responses.Where(r => !r.Key.StartsWith('2')).ToList();
            problems.ShouldNotBeEmpty();
            foreach (var (status, response) in problems)
            {
                response!["description"]!.GetValue<string>().StartsWith("Possible codes: ", StringComparison.Ordinal).ShouldBeTrue($"{route} {status}");
            }
        }

        executes.ShouldBeGreaterThan(0);
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
