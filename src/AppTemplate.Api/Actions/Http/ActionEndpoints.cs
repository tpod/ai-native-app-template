using System.Text.Json;
using AppTemplate.Api.Mcp;
using AppTemplate.Domain.Common;
using OneOf;

namespace AppTemplate.Api.Actions.Http;

public static class ActionEndpoints
{
    /// <summary>
    /// Maps one concrete route per registered descriptor:
    /// <c>POST /api/actions/{name}/preview</c> and <c>POST /api/actions/{name}</c> for mutations,
    /// <c>POST /api/queries/{name}</c> for queries. Errors are written as ProblemDetails.
    /// </summary>
    /// <remarks>Resolving the registry here builds it, so registration errors fail startup.</remarks>
    public static IEndpointRouteBuilder MapActions(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var registry = app.ServiceProvider.GetRequiredService<ActionRegistry>();

        foreach (var action in registry.All)
        {
            if (action.Kind == ActionKind.Mutation)
            {
                var previewName = ToolDescriptions.PreviewPrefix + action.Name;
                Describe(
                    app.MapPost($"/api/actions/{action.Name}/preview", (HttpContext http, ActionPipeline pipeline, CancellationToken ct)
                        => Handle(http, ct, body => pipeline.PreviewAsync(action, body, ct))),
                    action, previewName, typeof(PreviewResponse));
                Describe(
                    app.MapPost($"/api/actions/{action.Name}", (HttpContext http, ActionPipeline pipeline, CancellationToken ct)
                        => Handle(http, ct, body => pipeline.ExecuteAsync(action, body, ct))),
                    action, action.Name, typeof(ExecuteResponse));
            }
            else
            {
                Describe(
                    app.MapPost($"/api/queries/{action.Name}", (HttpContext http, ActionPipeline pipeline, CancellationToken ct)
                        => Handle(http, ct, body => pipeline.QueryAsync(action, body, ct))),
                    action, action.Name, action.OutputType!);
            }
        }

        return app;
    }

    /// <summary>OpenAPI metadata: operationId, description, request body, 200 type and one problem response per status the action can return.</summary>
    private static void Describe(RouteHandlerBuilder route, ActionDescriptor action, string operationId, Type success)
    {
        route.WithName(operationId)
            .WithDescription(ToolDescriptions.For(action, operationId))
            .Accepts(action.InputType, "application/json")
            .Produces(StatusCodes.Status200OK, success, "application/json");

        IEnumerable<Type> errorTypes = action.HasValidator ? [.. action.ErrorTypes, typeof(ValidationFailed)] : action.ErrorTypes;
        foreach (var status in errorTypes
            .Select(t => AppErrorProblemDetails.StatusFor(ErrorMetadata.For(t).Kind))
            .Distinct()
            .Order())
        {
            route.ProducesProblem(status);
        }
    }

    private static async Task<IResult> Handle<T>(HttpContext http, CancellationToken ct, Func<JsonElement, Task<OneOf<T, AppError>>> run)
        where T : notnull
    {
        var body = await ReadBodyAsync(http.Request, ct);
        if (body.TryPickT1(out var malformed, out var element))
        {
            return AppErrorProblemDetails.ToResult(malformed, http);
        }

        var result = await run(element);
        return result.Match(
            ok => TypedResults.Ok<object>(ok),
            error => AppErrorProblemDetails.ToResult(error, http));
    }

    /// <summary>Reads the raw body as one JSON value. Empty or unparsable → <c>request.malformed</c>; the shape is checked by the pipeline.</summary>
    private static async Task<OneOf<JsonElement, RequestMalformed>> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length == 0)
        {
            return new RequestMalformed("The request body is empty; send a JSON object.", "$");
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray());
            return document.RootElement.Clone();
        }
        catch (JsonException e)
        {
            return new RequestMalformed(
                $"The request body is not valid JSON (line {e.LineNumber + 1}, byte {e.BytePositionInLine + 1}).", e.Path ?? "$");
        }
    }
}
