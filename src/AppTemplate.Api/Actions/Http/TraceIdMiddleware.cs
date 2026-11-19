using System.Diagnostics;

namespace AppTemplate.Api.Actions.Http;

/// <summary>Adds <c>X-Trace-Id</c> to every response, set at response start so it survives exception-handler resets.</summary>
public sealed class TraceIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Trace-Id";

    public static string Resolve(HttpContext context)
        => Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

    public Task InvokeAsync(HttpContext context)
    {
        var traceId = Resolve(context);
        context.Response.OnStarting(static state =>
        {
            var (ctx, id) = ((HttpContext, string))state;
            ctx.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        }, (context, traceId));
        return next(context);
    }
}
