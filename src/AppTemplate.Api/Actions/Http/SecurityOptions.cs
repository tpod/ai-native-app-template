namespace AppTemplate.Api.Actions.Http;

/// <summary>The <c>Security</c> configuration section.</summary>
public sealed class SecurityOptions
{
    public const string Section = "Security";

    /// <summary>
    /// Browser origins (<c>scheme://host[:port]</c>, exact, case-insensitive) allowed to call <c>/api</c> and <c>/mcp</c>,
    /// in addition to loopback origins (<c>http(s)://localhost</c>, <c>127.0.0.1</c> or <c>[::1]</c> on any port),
    /// which are always allowed. A deployed app that serves its own UI must list its public origin here.
    /// </summary>
    public List<string> AllowedOrigins { get; } = [];
}
