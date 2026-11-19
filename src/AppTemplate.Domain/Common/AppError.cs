namespace AppTemplate.Domain.Common;

public abstract record AppError : IAppError
{
    public string Code => ErrorMetadata.For(GetType()).Code;
    public string Title => ErrorMetadata.For(GetType()).Title;
    public ErrorKind Kind => ErrorMetadata.For(GetType()).Kind;
    public abstract string Message { get; }
    public abstract string Suggestion { get; }
    public virtual object? Details => null;
}
