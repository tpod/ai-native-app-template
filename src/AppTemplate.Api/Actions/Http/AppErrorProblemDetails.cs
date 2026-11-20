using Microsoft.AspNetCore.Http;
using AppTemplate.Domain.Common;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails; // type lives in the Mvc namespace; no MVC controllers are used

namespace AppTemplate.Api.Actions.Http;

public static class AppErrorProblemDetails
{
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Precondition => StatusCodes.Status409Conflict,
        ErrorKind.BadRequest => StatusCodes.Status400BadRequest,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Unexpected => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static ProblemDetails ToProblemDetails(IAppError error, string traceId, string instance)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://errors.apptemplate.dev/{error.Code}",
            Title = error.Title,
            Status = StatusFor(error.Kind),
            Detail = error.Message,
            Instance = instance,
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["details"] = error.Details;
        problem.Extensions["suggestion"] = error.Suggestion;
        problem.Extensions["traceId"] = traceId;
        return problem;
    }

    public static IResult ToResult(IAppError error, HttpContext ctx)
    {
        var problem = ToProblemDetails(error, TraceIdMiddleware.Resolve(ctx), ctx.Request.Path.Value ?? "/");
        return TypedResults.Problem(problem);
    }
}
