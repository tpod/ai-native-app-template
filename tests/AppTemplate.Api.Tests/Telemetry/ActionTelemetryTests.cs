using System.Diagnostics;
using System.Diagnostics.Metrics;
using AppTemplate.Api.Actions;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Telemetry;

public sealed class ActionTelemetryTests : ApiTestBase
{
    private Activity Span(ApiResponse response, string name)
        => Factory.Activities.ForTrace(response.TraceId).Where(a => a.DisplayName == name).ToList().ShouldHaveSingleItem();

    [Fact]
    public async Task Preview_Success_EmitsSpanWithSuccessOutcome()
    {
        var p = await Preview("fixture_create_note", new { text = "x" });

        var span = Span(p, "action fixture_create_note preview");
        span.GetTagItem("action.name").ShouldBe("fixture_create_note");
        span.GetTagItem("action.phase").ShouldBe("preview");
        span.GetTagItem("action.outcome").ShouldBe("success");
        span.GetTagItem("aggregate.type").ShouldBe("Note");
        span.GetTagItem("aggregate.id").ShouldBe(IdFromChanges(p));
        span.GetTagItem("preview.stale_reason").ShouldBeNull();
        span.Parent.ShouldNotBeNull();
    }

    [Fact]
    public async Task Execute_Stale_TagsOutcomeAndStaleReason()
    {
        var id = IdFromChanges(await CreateNote("hello"));
        var p = await Preview("fixture_edit_note", new { noteId = id, text = "world" });
        (await Execute("fixture_edit_note", new { noteId = id, text = "world" }, TokenOf(p))).Status.ShouldBe(System.Net.HttpStatusCode.OK);

        var stale = await Execute("fixture_edit_note", new { noteId = id, text = "world" }, TokenOf(p));

        var span = Span(stale, "action fixture_edit_note execute");
        span.GetTagItem("action.outcome").ShouldBe("preview.stale");
        span.GetTagItem("preview.stale_reason").ShouldBe("version_changed");
        span.GetTagItem("aggregate.type").ShouldBe("Note");
        span.GetTagItem("aggregate.id").ShouldBe(id);
    }

    [Fact]
    public async Task Query_NotFound_TagsErrorCodeAsOutcome()
    {
        var q = await Query("fixture_get_note", new { noteId = Guid.NewGuid() });

        Span(q, "action fixture_get_note query").GetTagItem("action.outcome").ShouldBe("note.not_found");
    }

    [Fact]
    public async Task Execute_Unhandled_TagsInternalUnexpectedAndErrorStatus()
    {
        var p = await Preview("fixture_explode", new { });
        p.Status.ShouldBe(System.Net.HttpStatusCode.InternalServerError);

        var span = Span(p, "action fixture_explode preview");
        span.GetTagItem("action.outcome").ShouldBe("internal.unexpected");
        span.Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Preview_Calls_RecordCounterAndHistogramWithTags()
    {
        var calls = new List<(long Value, Dictionary<string, object?> Tags)>();
        var durations = new List<(double Value, Dictionary<string, object?> Tags)>();
        // Other tests run hosts in parallel; only this host's meter factory scope is observed.
        var scope = Factory.Services.GetRequiredService<IMeterFactory>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) =>
        {
            if (i.Meter.Name == "AppTemplate.Actions" && ReferenceEquals(i.Meter.Scope, scope))
            {
                l.EnableMeasurementEvents(i);
            }
        };
        listener.SetMeasurementEventCallback<long>((i, v, t, _) =>
        {
            if (i.Name == "app.actions.calls") { lock (calls) { calls.Add((v, ToDict(t))); } }
        });
        listener.SetMeasurementEventCallback<double>((i, v, t, _) =>
        {
            if (i.Name == "app.actions.duration") { lock (durations) { durations.Add((v, ToDict(t))); } }
        });
        listener.Start();

        await Preview("fixture_create_note", new { text = "x" });
        await Query("fixture_get_note", new { noteId = Guid.NewGuid() });

        lock (calls)
        {
            calls.ShouldContain(c => (string?)c.Tags["action.name"] == "fixture_create_note"
                && (string?)c.Tags["action.phase"] == "preview" && (string?)c.Tags["action.outcome"] == "success" && c.Value == 1);
            calls.ShouldContain(c => (string?)c.Tags["action.name"] == "fixture_get_note"
                && (string?)c.Tags["action.outcome"] == "note.not_found");
        }

        lock (durations)
        {
            durations.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task EveryFixtureAction_EachPhase_EmitsSpanWithOutcome()
    {
        var registry = Factory.Services.GetRequiredService<ActionRegistry>();
        var fixtures = registry.All.Where(a => a.Name.StartsWith("fixture_", StringComparison.Ordinal)).ToList();
        fixtures.Count.ShouldBeGreaterThan(0);
        var noteId = IdFromChanges(await CreateNote("seed"));
        HashSet<string> throwing = ["fixture_explode", "fixture_untracked_edit"]; // preview throws, so no execute

        var checkedSpans = 0;
        foreach (var action in fixtures)
        {
            object input = action.Name switch
            {
                "fixture_create_note" => new { text = "hi" },
                "fixture_edit_note" => new { noteId, text = "edited" },
                "fixture_lock_note" => new { noteId },
                "fixture_get_note" => new { noteId },
                "fixture_list_notes" => new { },
                "fixture_explode" => new { },
                "fixture_untracked_edit" => new { noteId, text = "edited" },
                _ => throw new InvalidOperationException($"No known-valid input for {action.Name}; add it here."),
            };

            var phases = new List<(string Phase, ApiResponse Response)>();
            if (action.Kind == ActionKind.Query)
            {
                phases.Add(("query", await Query(action.Name, input)));
            }
            else
            {
                var p = await Preview(action.Name, input);
                phases.Add(("preview", p));
                if (!throwing.Contains(action.Name))
                {
                    phases.Add(("execute", await Execute(action.Name, input, TokenOf(p))));
                }
            }

            foreach (var (phase, response) in phases)
            {
                var span = Span(response, $"action {action.Name} {phase}");
                span.GetTagItem("action.name").ShouldBe(action.Name);
                span.GetTagItem("action.phase").ShouldBe(phase);
                var outcome = span.GetTagItem("action.outcome").ShouldBeOfType<string>();
                outcome.ShouldBe(throwing.Contains(action.Name) ? "internal.unexpected" : "success");
                checkedSpans++;
            }
        }

        checkedSpans.ShouldBe(fixtures.Count + fixtures.Count(a => a.Kind == ActionKind.Mutation && !throwing.Contains(a.Name)));
    }

    private async Task<ApiResponse> CreateNote(string text)
    {
        var (p, e) = await PreviewAndExecute("fixture_create_note", new { text });
        e.Status.ShouldBe(System.Net.HttpStatusCode.OK);
        return p;
    }

    private static Dictionary<string, object?> ToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var d = new Dictionary<string, object?>();
        foreach (var t in tags)
        {
            d[t.Key] = t.Value;
        }

        return d;
    }
}
