using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AppTemplate.Api.Actions.Preview;

internal static class CanonicalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    /// <summary>Hex SHA-256 over the compact JSON with every object's keys sorted ordinally.</summary>
    public static string Hash(JsonNode? node)
    {
        var json = Sort(node)?.ToJsonString() ?? "null";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => SortObject(obj),
        JsonArray arr => new JsonArray([.. arr.Select(Sort)]),
        null => null,
        _ => node.DeepClone(),
    };

    private static JsonObject SortObject(JsonObject obj)
    {
        var sorted = new JsonObject();
        foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sorted.Add(key, Sort(value));
        }

        return sorted;
    }
}
