namespace AppTemplate.Api.Actions;

/// <summary>Who called an execute: the channel and, for MCP, the client name when known. Stored on each history row.</summary>
/// <param name="Channel"><c>http</c>, <c>mcp</c> or <c>in_process</c>.</param>
/// <param name="Client">The MCP client's name, or null when the channel has none or the client did not say.</param>
public sealed record ActionCaller(string Channel, string? Client)
{
    public static ActionCaller Http { get; } = new("http", null);

    public static ActionCaller InProcess { get; } = new("in_process", null);

    /// <summary>
    /// The client name is client-supplied, so it is trimmed and cut to <see cref="ClientMaxLength"/>: an over-long name
    /// must never fail the save (providers such as Postgres enforce the column length).
    /// </summary>
    public static ActionCaller Mcp(string? client)
    {
        var name = client?.Trim();
        return new("mcp", string.IsNullOrEmpty(name) ? null : name[..Math.Min(name.Length, ClientMaxLength)]);
    }

    /// <summary>Matches the <c>History.Client</c> column length.</summary>
    public const int ClientMaxLength = 200;
}
