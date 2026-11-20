using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using AppTemplate.Api.Actions;
using ModelContextProtocol.Protocol;

namespace AppTemplate.Api.Mcp;

/// <summary>Builds the MCP tool list from the action registry (spec §7.6).</summary>
public static class McpToolFactory
{
    public const string PreviewTokenProperty = "previewToken";

    /// <summary>
    /// Mutations yield <c>preview_{name}</c> (read-only) and <c>{name}</c> (not read-only, destructive when flagged);
    /// queries yield <c>{name}</c> (read-only). Input schemas come from <see cref="JsonSchemaExporter"/>; execute
    /// tools add a required string <c>previewToken</c>.
    /// </summary>
    /// <param name="json">Serializer options with a <see cref="JsonSerializerOptions.TypeInfoResolver"/>.</param>
    public static IReadOnlyList<Tool> Build(ActionRegistry registry, JsonSerializerOptions json)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(json);

        List<Tool> tools = [];
        foreach (var d in registry.All)
        {
            if (d.Kind == ActionKind.Mutation)
            {
                var preview = ToolDescriptions.PreviewPrefix + d.Name;
                tools.Add(Create(d, preview, InputSchema(d, json, withToken: false), readOnly: true, destructive: null));
                tools.Add(Create(d, d.Name, InputSchema(d, json, withToken: true), readOnly: false, destructive: d.Destructive));
            }
            else
            {
                var note = d.OutputType is { } output && json.GetTypeInfo(output).Kind == JsonTypeInfoKind.Enumerable
                    ? $" Returns the list under structuredContent.{McpSetup.ItemsProperty}."
                    : null;
                tools.Add(Create(d, d.Name, InputSchema(d, json, withToken: false), readOnly: true, destructive: null, note));
            }
        }

        return tools;
    }

    private static Tool Create(ActionDescriptor d, string name, JsonElement schema, bool readOnly, bool? destructive, string? note = null) => new()
    {
        Name = name,
        Description = ToolDescriptions.For(d, name) + note,
        InputSchema = schema,
        Annotations = new ToolAnnotations { ReadOnlyHint = readOnly, DestructiveHint = destructive },
    };

    private static JsonElement InputSchema(ActionDescriptor d, JsonSerializerOptions json, bool withToken)
    {
        var node = JsonSchemaExporter.GetJsonSchemaAsNode(json, d.InputType);
        var schema = node as JsonObject ?? new JsonObject();

        // A reference-type root exports as ["object", "null"]; MCP requires an object schema.
        schema["type"] = "object";

        if (withToken)
        {
            if (schema["properties"] is not JsonObject properties)
            {
                properties = [];
                schema["properties"] = properties;
            }

            properties[PreviewTokenProperty] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = $"The previewToken returned by preview_{d.Name} for this exact input.",
            };

            if (schema["required"] is not JsonArray required)
            {
                required = [];
                schema["required"] = required;
            }

            required.Add(PreviewTokenProperty);
        }

        return JsonSerializer.SerializeToElement<JsonNode>(schema, json);
    }
}
