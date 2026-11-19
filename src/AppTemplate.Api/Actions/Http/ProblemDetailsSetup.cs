using Microsoft.AspNetCore.Http;
using AppTemplate.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace AppTemplate.Api.Actions.Http;

public static class ProblemDetailsSetup
{
    public static IServiceCollection AddAppProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = Customize);
        services.AddExceptionHandler<UnexpectedExceptionHandler>();
        return services;
    }

    /// <summary>Trace header outermost, then the exception handler, then status-code pages (which only write when the response has no body yet).</summary>
    public static IApplicationBuilder UseAppProblemDetails(this IApplicationBuilder app)
    {
        app.UseMiddleware<TraceIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    private static void Customize(ProblemDetailsContext context)
    {
        var extensions = context.ProblemDetails.Extensions;
        extensions["traceId"] = TraceIdMiddleware.Resolve(context.HttpContext);
        if (extensions.ContainsKey("code"))
        {
            return;
        }

        // Framework-generated response (plain 404/405/415, ...): give it the same shape as an AppError.
        IAppError error = context.ProblemDetails.Status == StatusCodes.Status404NotFound
            ? new RequestNotFound(context.HttpContext.Request.Path.Value ?? "/")
            : new RequestMalformed(
                context.ProblemDetails.Title ?? "The request could not be processed.", Path: null);
        extensions["code"] = error.Code;
        extensions.TryAdd("details", error.Details);
        extensions.TryAdd("suggestion", error.Suggestion);
    }

    /// <summary>Maps any unhandled exception to <see cref="InternalUnexpected"/>; exception text never reaches the body.</summary>
    private sealed class UnexpectedExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<UnexpectedExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var traceId = TraceIdMiddleware.Resolve(httpContext);
            // The middleware suppresses its own UnhandledException log when an IExceptionHandler handles the exception
            // (ExceptionHandlerOptions.SuppressDiagnosticsCallback default), so this is the single log line per error response.
            var error = new InternalUnexpected();
            logger.LogError(exception, "Unhandled exception for {Method} {Path}: {Code} (traceId {TraceId})",
                httpContext.Request.Method, httpContext.Request.Path, error.Code, traceId);

            var problem = AppErrorProblemDetails.ToProblemDetails(
                error, traceId, httpContext.Request.Path.Value ?? "/");
            httpContext.Response.StatusCode = problem.Status!.Value;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
            });
        }
    }
}
