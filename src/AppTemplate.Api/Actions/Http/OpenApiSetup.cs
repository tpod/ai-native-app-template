using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AppTemplate.Api.Actions.Http;

/// <summary>
/// OpenAPI document for the generated action endpoints, served at <c>/openapi/v1.json</c>.
/// Static metadata comes from endpoint metadata set in <see cref="ActionEndpoints"/>; the transformers here add only
/// what metadata cannot express: <c>previewToken</c> on execute requests and the problem-details extension members.
/// </summary>
public static class OpenApiSetup
{
    private const string Json = "application/json";
    private const string ProblemSchemaId = "ProblemDetails";

    public static IServiceCollection AddAppOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOpenApi(options =>
        {
            options.AddOperationTransformer(AddPreviewTokenToExecuteRequest);
            options.AddDocumentTransformer(AddProblemExtensions);
        });
        return services;
    }

    public static IEndpointRouteBuilder MapAppOpenApi(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapOpenApi("/openapi/{documentName}.json");
        return app;
    }

    /// <summary>Execute = <c>POST /api/actions/{name}</c>: the body is the input plus a required <c>previewToken</c>.</summary>
    private static Task AddPreviewTokenToExecuteRequest(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        DescribeErrorCodes(operation, context);
        var path = context.Description.RelativePath ?? "";
        var isExecute = path.StartsWith("api/actions/", StringComparison.Ordinal)
            && !path.EndsWith("/preview", StringComparison.Ordinal);
        if (!isExecute
            || operation.RequestBody?.Content is not { } content
            || !content.TryGetValue(Json, out var media)
            || media.Schema is not { } input)
        {
            return Task.CompletedTask;
        }

        media.Schema = new OpenApiSchema
        {
            AllOf =
            [
                input,
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["previewToken"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Description = "Token from the matching preview operation; authorizes this execute.",
                        },
                    },
                    Required = new HashSet<string> { "previewToken" },
                },
            ],
        };
        return Task.CompletedTask;
    }

    /// <summary>Sets each problem response's description to the codes it can carry.</summary>
    private static void DescribeErrorCodes(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        var errors = context.Description.ActionDescriptor.EndpointMetadata.OfType<ActionErrorResponses>().FirstOrDefault();
        if (errors is null || operation.Responses is null)
        {
            return;
        }

        foreach (var (status, codes) in errors.CodesByStatus)
        {
            if (operation.Responses.TryGetValue(status.ToString(System.Globalization.CultureInfo.InvariantCulture), out var response)
                && response is OpenApiResponse concrete)
            {
                concrete.Description = $"Possible codes: {string.Join(", ", codes)}.";
            }
        }
    }

    /// <summary>Adds the four extension members every problem response carries to the shared ProblemDetails schema.</summary>
    private static Task AddProblemExtensions(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Servers = []; // host-dependent (the test host reports localhost); clients choose their own base URL
        if (document.Components?.Schemas is not { } schemas
            || !schemas.TryGetValue(ProblemSchemaId, out var schema)
            || schema is not OpenApiSchema problem)
        {
            return Task.CompletedTask;
        }

        problem.Properties ??= new Dictionary<string, IOpenApiSchema>();
        problem.Properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Stable machine-readable error code, for example note.locked." };
        problem.Properties["details"] = new OpenApiSchema { Description = "Error-specific structured data; may be null." };
        problem.Properties["suggestion"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "What the caller can do to fix the problem." };
        problem.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Correlates the response with server logs and traces." };
        return Task.CompletedTask;
    }
}
