namespace AppTemplate.Domain.Common;

public interface IAppError
{
    string Code { get; }
    string Title { get; }
    ErrorKind Kind { get; }
    string Message { get; }
    object? Details { get; }
    string Suggestion { get; }
}
