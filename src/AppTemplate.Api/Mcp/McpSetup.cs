using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AppTemplate.Api.Actions;
using AppTemplate.Api.Actions.Http;
using AppTemplate.Domain.Common;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OneOf;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace AppTemplate.Api.Mcp;

/// <summary>
/// The MCP server (spec §7.6): Streamable HTTP at <c>/mcp</c>, tools generated from the <see cref="ActionRegistry"/>,
/// every call run through the <see cref="ActionPipeline"/>.
/// </summary>
/// <remarks>
/// <para>
/// The transport is stateless (<see cref="ModelContextProtocol.AspNetCore.HttpServerTransportOptions.Stateless"/>):
/// each POST is answered within its own HTTP request and no session is kept, so the server runs behind any load
/// balancer and under <c>WebApplicationFactory</c>'s in-memory handler. Server-to-client requests (sampling,
/// elicitation) are therefore unavailable.
/// </para>
/// <para>
/// Both handlers read the registry and services at request time. Results reuse the HTTP JSON options; an
/// <see cref="AppError"/> becomes <c>isError</c> with the ProblemDetails JSON (<c>instance</c> = <c>mcp:{tool}</c>),
/// and an unhandled exception becomes <c>internal.unexpected</c> without exception text.
/// </para>
/// </remarks>
public static partial class McpSetup
{
    public const string ServerName = "AppTemplate";
    public const string UnknownToolSuggestion = "Call tools/list to see available tools.";

    /// <summary>The <c>structuredContent</c> member that holds a list result.</summary>
    public const string ItemsProperty = "items";

    /// <summary>The <c>structuredContent</c> member that holds a scalar result.</summary>
    public const string ValueProperty = "value";

    private const string JsonKey = "AppTemplate.Mcp.Json";

    private enum Phase { Preview, Execute, Query }

    public static IServiceCollection AddAppMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The HTTP options, or a copy with a reflection resolver when they have none (the schema exporter needs one).
        // The shared instance is never mutated.
        services.TryAddKeyedSingleton<JsonSerializerOptions>(JsonKey, static (sp, _) =>
        {
            var shared = sp.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
            return shared.TypeInfoResolver is null
                ? new JsonSerializerOptions(shared) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }
                : shared;
        });

        services.AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation
                {
                    Name = ServerName,
                    Version = typeof(McpSetup).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                };
                o.ServerInstructions = McpInstructions.Text;
            })
            .WithHttpTransport(o => o.Stateless = true)
            .WithListToolsHandler(ListToolsAsync)
            .WithCallToolHandler(CallToolAsync);

        return services;
    }

    private static ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken ct)
    {
        var services = ServicesOf(request);
        var tools = McpToolFactory.Build(services.GetRequiredService<ActionRegistry>(), JsonOf(services));
        return ValueTask.FromResult(new ListToolsResult { Tools = [.. tools] });
    }

    private static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken ct)
    {
        var services = ServicesOf(request);
        var json = JsonOf(services);
        var tool = request.Params?.Name ?? string.Empty;
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();

        if (Resolve(services.GetRequiredService<ActionRegistry>(), tool) is not var (action, phase))
        {
            return ErrorResult(new McpToolNotFound(tool), tool, traceId, json);
        }

        var body = JsonSerializer.SerializeToElement(request.Params?.Arguments ?? new Dictionary<string, JsonElement>(), json);
        var pipeline = services.GetRequiredService<ActionPipeline>();
        try
        {
            var result = phase switch
            {
                Phase.Preview => Widen(await pipeline.PreviewAsync(action, body, ct)),
                Phase.Execute => Widen(await pipeline.ExecuteAsync(action, body, ct)),
                _ => await pipeline.QueryAsync(action, body, ct),
            };
            return result.Match(ok => SuccessResult(ok, json), error => ErrorResult(error, tool, traceId, json));
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var error = new InternalUnexpected();
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(McpSetup).FullName!);
            LogUnexpected(logger, e, tool, error.Code, traceId);
            return ErrorResult(error, tool, traceId, json);
        }
    }

    /// <summary>Exact name first (execute or query), then <c>preview_{mutation}</c>.</summary>
    private static (ActionDescriptor Action, Phase Phase)? Resolve(ActionRegistry registry, string tool)
    {
        if (registry.Find(tool) is { } action)
        {
            return (action, action.Kind == ActionKind.Mutation ? Phase.Execute : Phase.Query);
        }

        if (tool.StartsWith(ToolDescriptions.PreviewPrefix, StringComparison.Ordinal)
            && registry.Find(tool[ToolDescriptions.PreviewPrefix.Length..]) is { Kind: ActionKind.Mutation } mutation)
        {
            return (mutation, Phase.Preview);
        }

        return null;
    }

    private static OneOf<object, AppError> Widen<T>(OneOf<T, AppError> result)
        where T : notnull
        => result.Match<OneOf<object, AppError>>(ok => ok, error => error);

    /// <summary>
    /// MCP requires <c>structuredContent</c> to be a JSON object, so a non-object result is wrapped: an array as
    /// <c>{ "items": [...] }</c>, any other value as <c>{ "value": x }</c>. The text block carries the same (wrapped)
    /// JSON, so both views agree. No <c>outputSchema</c> is declared.
    /// </summary>
    private static CallToolResult SuccessResult(object response, JsonSerializerOptions json)
    {
        var element = ObjectShaped(JsonSerializer.SerializeToElement(response, response.GetType(), json), json);
        return new CallToolResult
        {
            StructuredContent = element,
            Content = [new TextContentBlock { Text = element.GetRawText() }],
        };
    }

    private static JsonElement ObjectShaped(JsonElement element, JsonSerializerOptions json) => element.ValueKind switch
    {
        JsonValueKind.Object => element,
        JsonValueKind.Array => JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [ItemsProperty] = element }, json),
        _ => JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [ValueProperty] = element }, json),
    };

    private static CallToolResult ErrorResult(IAppError error, string tool, string traceId, JsonSerializerOptions json)
    {
        var problem = AppErrorProblemDetails.ToProblemDetails(error, traceId, $"mcp:{tool}");
        var element = JsonSerializer.SerializeToElement(problem, json);
        return new CallToolResult
        {
            IsError = true,
            StructuredContent = element,
            Content = [new TextContentBlock { Text = element.GetRawText() }],
        };
    }

    private static IServiceProvider ServicesOf(MessageContext request)
        => request.Services ?? throw new InvalidOperationException("The MCP request has no service provider.");

    private static JsonSerializerOptions JsonOf(IServiceProvider services)
        => services.GetRequiredKeyedService<JsonSerializerOptions>(JsonKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for MCP tool {Tool}: {Code} (traceId {TraceId})")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string tool, string code, string traceId);
}

/// <summary>A <c>tools/call</c> for a name that is not in <c>tools/list</c>.</summary>
[Error("request.not_found", ErrorKind.NotFound, "Tool not found")]
public sealed record McpToolNotFound(string Tool) : AppError
{
    public override string Message => $"No tool named '{Tool}' exists.";
    public override string Suggestion => McpSetup.UnknownToolSuggestion;
    public override object? Details => new { tool = Tool };
}
