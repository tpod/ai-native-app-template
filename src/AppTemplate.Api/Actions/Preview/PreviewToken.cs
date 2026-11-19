namespace AppTemplate.Api.Actions.Preview;

public sealed record AggregateVersion(string Type, string Id, int Version);

public sealed record PreviewTokenPayload(
    string Action,
    string InputHash,
    IReadOnlyList<AggregateVersion> AggregateVersions,
    string ChangesHash,
    IReadOnlyList<Guid> AllocatedIds,
    DateTimeOffset ExpiresAt);
