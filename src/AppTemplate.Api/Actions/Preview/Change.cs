using System.Text.Json.Nodes;

namespace AppTemplate.Api.Actions.Preview;

/// <param name="Op">One of <c>added</c>, <c>modified</c>, <c>deleted</c>.</param>
public sealed record Change(string Entity, string Id, string Op, string Field, JsonNode? Before, JsonNode? After);

public sealed record SideEffect(string Event, JsonNode Payload);
