using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace AppTemplate.Api.Actions.Http;

/// <summary>
/// DNS-rebinding and cross-site protection for <c>/api</c> and <c>/mcp</c> (the MCP spec requires servers to validate
/// <c>Origin</c>). A request that carries an <c>Origin</c> header must come from a loopback origin or one listed in
/// <see cref="SecurityOptions.AllowedOrigins"/>; otherwise it gets <c>403 request.forbidden_origin</c> before any
/// endpoint runs. Requests without an <c>Origin</c> (agents, MCP clients, curl) pass.
/// </summary>
/// <remarks>
/// Same-origin is deliberately not inferred from the <c>Host</c> header: under DNS rebinding the attacker's page and
/// <c>Host</c> share the attacker's name, so that check would pass. The Vite dev proxy forwards the browser's
/// loopback <c>Origin</c> unchanged (<c>changeOrigin</c> rewrites only <c>Host</c>), so the dev UI passes.
/// </remarks>
public sealed partial class OriginCheckMiddleware(RequestDelegate next, IOptions<SecurityOptions> options, ILogger<OriginCheckMiddleware> logger)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return next(context);
        }

        var origin = context.Request.Headers[HeaderNames.Origin].ToString();
        if (origin.Length == 0 || IsAllowed(origin, options.Value.AllowedOrigins))
        {
            return next(context);
        }

        LogRejected(logger, origin, path.Value ?? "/");
        return AppErrorProblemDetails.ToResult(new RequestForbiddenOrigin(origin), context).ExecuteAsync(context);
    }

    /// <summary>True for an http(s) loopback origin on any port, or an exact (case-insensitive) configured origin.</summary>
    public static bool IsAllowed(string origin, IEnumerable<string> configured)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(configured);

        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.IsLoopback)
        {
            return true;
        }

        var normalized = origin.TrimEnd('/');
        return configured.Any(o => string.Equals(o.TrimEnd('/'), normalized, StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected request to {Path} from origin {Origin}: not in Security:AllowedOrigins")]
    private static partial void LogRejected(ILogger logger, string origin, string path);
}
