using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Actions;

[Error("preview.token_invalid", ErrorKind.BadRequest, "Preview token invalid")]
public sealed record PreviewTokenInvalid : AppError
{
    public override string Message => "The preview token is malformed or its signature does not verify.";
    public override string Suggestion => "Pass the previewToken exactly as returned by the preview call.";
}

[Error("preview.token_mismatch", ErrorKind.Precondition, "Preview token does not match request")]
public sealed record PreviewTokenMismatch(string Expected, string Actual) : AppError
{
    public override string Message => $"The preview token was issued for '{Expected}' but the request is for '{Actual}'.";
    public override string Suggestion => "Execute with the same action and input that were previewed.";
    public override object? Details => new { expected = Expected, actual = Actual };
}

[Error("preview.expired", ErrorKind.Precondition, "Preview expired")]
public sealed record PreviewExpired(DateTimeOffset ExpiredAt) : AppError
{
    public override string Message => $"The preview expired at {ExpiredAt:O}.";
    public override string Suggestion => "Request a new preview and execute it before it expires.";
    public override object? Details => new { expiredAt = ExpiredAt };
}

[Error("preview.stale", ErrorKind.Precondition, "Preview is stale")]
public sealed record PreviewStale(string Action, string Reason, object? Cause) : AppError
{
    public override string Message => $"The preview for '{Action}' is stale: {Reason}.";
    public override string Suggestion => $"Call preview_{Action} again and review the new preview.";
    public override object? Details => new { action = Action, reason = Reason, cause = Cause };
}

[Error("preview.already_executed", ErrorKind.Conflict, "Preview already executed")]
public sealed record PreviewAlreadyExecuted(string Action) : AppError
{
    public override string Message => $"The preview for '{Action}' was already executed.";
    public override string Suggestion => "Do not retry; query the resource to confirm the earlier result.";
    public override object? Details => new { action = Action };
}

[Error("request.malformed", ErrorKind.BadRequest, "Request malformed")]
public sealed record RequestMalformed(string Detail, string? Path) : AppError
{
    public override string Message => Detail;
    public override string Suggestion => "Fix the request body or parameters and send it again.";
    public override object? Details => new { path = Path };
}

[Error("request.not_found", ErrorKind.NotFound, "Resource not found")]
public sealed record RequestNotFound(string Path) : AppError
{
    public override string Message => $"No endpoint exists at '{Path}'.";
    public override string Suggestion => "Check the path and HTTP method against the API description.";
    public override object? Details => new { path = Path };
}

[Error("request.forbidden_origin", ErrorKind.Forbidden, "Origin not allowed")]
public sealed record RequestForbiddenOrigin(string Origin) : AppError
{
    public override string Message => $"Browser requests from origin '{Origin}' are not allowed.";
    public override string Suggestion =>
        "If this origin is trusted, the server operator must add it to the Security:AllowedOrigins configuration. "
        + "Agents and command-line clients send no Origin header and are not affected.";
    public override object? Details => new { origin = Origin };
}

[Error("internal.unexpected", ErrorKind.Unexpected, "Unexpected error")]
public sealed record InternalUnexpected : AppError
{
    public override string Message => "An unexpected error occurred.";
    public override string Suggestion => "This is not caused by your input. Report the traceId; retrying may not help.";
}
