namespace AppTemplate.Api.Tests.Infrastructure;

public enum Case { HappyPath, Stale }

/// <summary>Marks which action and error code (or case) a test covers; a coverage check can read it by reflection.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CoversAttribute : Attribute
{
    public CoversAttribute(string action, string errorCode)
    {
        Action = action;
        ErrorCode = errorCode;
    }

    public CoversAttribute(string action, Case @case)
    {
        Action = action;
        Case = @case;
    }

    public string Action { get; }
    public string? ErrorCode { get; }
    public Case? Case { get; }
}
