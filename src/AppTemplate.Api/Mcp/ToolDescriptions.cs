using AppTemplate.Api.Actions;

namespace AppTemplate.Api.Mcp;

/// <summary>Agent-facing descriptions of the tools (MCP) and operations (OpenAPI) generated for an action.</summary>
public static class ToolDescriptions
{
    public const string PreviewPrefix = ActionRegistry.PreviewPrefix;

    /// <summary>
    /// The description of <paramref name="tool"/>, which is <c>preview_{name}</c> or <c>{name}</c> of <paramref name="d"/>:
    /// the action description, a preview note for preview tools, the possible error codes, and for execute tools
    /// the previewToken and user-confirmation requirement.
    /// </summary>
    public static string For(ActionDescriptor d, string tool)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(tool);

        var isPreview = d.Kind == ActionKind.Mutation && string.Equals(tool, PreviewPrefix + d.Name, StringComparison.Ordinal);
        var isExecute = d.Kind == ActionKind.Mutation && !isPreview;
        List<string> parts = [d.Description];

        if (isPreview)
        {
            parts.Add($"Preview only: saves nothing and returns summary, changes, sideEffects, warnings and the previewToken for {d.Name}.");
        }

        if (d.ErrorCodes.Count > 0)
        {
            parts.Add($"Possible error codes: {string.Join(", ", d.ErrorCodes)}.");
        }

        if (isExecute)
        {
            parts.Add($"Requires previewToken from preview_{d.Name}. Show the user the preview summary and changes and get their confirmation before calling this tool.");
        }

        return string.Join(" ", parts);
    }
}
