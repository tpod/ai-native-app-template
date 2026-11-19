namespace AppTemplate.Domain.Common;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ErrorAttribute(string code, ErrorKind kind, string title) : Attribute
{
    public string Code { get; } = code;
    public ErrorKind Kind { get; } = kind;
    public string Title { get; } = title;
}
