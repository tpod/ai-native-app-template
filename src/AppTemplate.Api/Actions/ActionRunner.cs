using System.Text.Json;
using System.Text.Json.Nodes;
using AppTemplate.Domain.Common;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using OneOf;

namespace AppTemplate.Api.Actions;

/// <summary>
/// Runs a mutation from in-process code (imports, scheduled jobs, admin tasks): preview, then execute with the
/// returned token, on the same pipeline as HTTP and MCP. Validation, stale checks, telemetry and history all apply,
/// and the history rows record channel <c>in_process</c>.
/// </summary>
/// <remarks>
/// The confirmation step is skipped by design: the calling code is the trusted decision-maker. Every state change
/// goes through an action (AGENTS.md golden rule 10), so in-process code uses this instead of changing aggregates or
/// calling <c>SaveChanges</c> itself. A <c>preview.stale</c> result is returned as is; the caller decides whether
/// to retry.
/// </remarks>
public sealed class ActionRunner(ActionRegistry registry, ActionPipeline pipeline, IOptions<JsonOptions> jsonOptions)
{
    /// <param name="action">The mutation name, e.g. <c>archive_project</c>.</param>
    /// <param name="input">An object serializing to the action's input record.</param>
    /// <param name="reason">Why the caller makes this change; stored in history. Optional.</param>
    /// <returns>The execute response, or the first error from preview or execute.</returns>
    public async Task<OneOf<ExecuteResponse, AppError>> RunAsync(string action, object input, string? reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (registry.Find(action) is not { Kind: ActionKind.Mutation } descriptor)
        {
            return new RequestNotFound($"/api/actions/{action}");
        }

        var json = jsonOptions.Value.SerializerOptions;
        var body = JsonSerializer.SerializeToNode(input, input.GetType(), json) as JsonObject ?? [];
        var preview = await pipeline.PreviewAsync(descriptor, JsonSerializer.SerializeToElement(body, json), ct);
        if (preview.TryPickT1(out var error, out var previewed))
        {
            return error;
        }

        body["previewToken"] = previewed.PreviewToken;
        body[ActionPipeline.ReasonProperty] = reason;
        return await pipeline.ExecuteAsync(descriptor, JsonSerializer.SerializeToElement(body, json), ActionCaller.InProcess, ct);
    }
}
