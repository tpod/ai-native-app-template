using System.Text.Json;
using System.Text.Json.Nodes;

namespace AppTemplate.Api.Actions.Preview;

public static class ChangeSetHasher
{
    /// <summary>Hex SHA-256 over canonical JSON. Changes are order-independent; side effects and warnings keep their order.</summary>
    public static string Hash(
        IReadOnlyList<Change> changes,
        IReadOnlyList<SideEffect> sideEffects,
        IReadOnlyList<PreviewWarning> warnings)
    {
        var ordered = changes
            .OrderBy(c => c.Entity, StringComparer.Ordinal)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Field, StringComparer.Ordinal)
            .ToList();
        var root = new JsonObject
        {
            ["changes"] = JsonSerializer.SerializeToNode(ordered, CanonicalJson.Options),
            ["sideEffects"] = JsonSerializer.SerializeToNode(sideEffects, CanonicalJson.Options),
            ["warnings"] = JsonSerializer.SerializeToNode(warnings, CanonicalJson.Options),
        };
        return CanonicalJson.Hash(root);
    }
}

public static class InputHasher
{
    /// <summary>Hashes the re-serialized deserialized input, so formatting and key order in the request do not matter.</summary>
    public static string Hash(object input, Type inputType, JsonSerializerOptions json)
        => CanonicalJson.Hash(JsonSerializer.SerializeToNode(input, inputType, json));
}
