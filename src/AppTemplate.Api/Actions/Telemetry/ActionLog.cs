namespace AppTemplate.Api.Actions.Telemetry;

/// <summary>Stable structured log messages for action phases.</summary>
public static partial class ActionLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Action {ActionName} {Phase} failed with {ErrorCode} (traceId {TraceId})")]
    public static partial void ActionFailed(ILogger logger, string actionName, string phase, string errorCode, string traceId);

    // Unhandled exceptions are logged once by the host's exception handlers; the pipeline does not call this.
    [LoggerMessage(Level = LogLevel.Error,
        Message = "Action {ActionName} {Phase} threw an unexpected exception (traceId {TraceId})")]
    public static partial void ActionUnexpected(ILogger logger, Exception exception, string actionName, string phase, string traceId);
}
