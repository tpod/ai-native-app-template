using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Actions.Http;

/// <summary>Endpoint metadata: the error codes an operation can return, grouped by HTTP status. Read by the OpenAPI transformer.</summary>
public sealed record ActionErrorResponses(IReadOnlyDictionary<int, IReadOnlyList<string>> CodesByStatus)
{
    // Every action endpoint can fail to read its body or throw.
    private static readonly Type[] Always = [typeof(RequestMalformed), typeof(InternalUnexpected)];

    // Execute additionally verifies the preview token.
    private static readonly Type[] ExecuteOnly =
    [
        typeof(PreviewTokenInvalid), typeof(PreviewTokenMismatch), typeof(PreviewExpired), typeof(PreviewStale), typeof(PreviewAlreadyExecuted),
    ];

    /// <summary>The action's own errors, the framework errors of every endpoint, and for execute the preview-token errors.</summary>
    public static ActionErrorResponses For(ActionDescriptor action, bool isExecute)
    {
        IEnumerable<Type> types = action.HasValidator ? [.. action.ErrorTypes, typeof(ValidationFailed)] : action.ErrorTypes;
        types = types.Concat(Always);
        if (isExecute)
        {
            types = types.Concat(ExecuteOnly);
        }

        var byStatus = types
            .Select(t => ErrorMetadata.For(t))
            .Select(a => (Status: AppErrorProblemDetails.StatusFor(a.Kind), a.Code))
            .GroupBy(x => x.Status)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)[.. g.Select(x => x.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        return new ActionErrorResponses(byStatus);
    }
}
