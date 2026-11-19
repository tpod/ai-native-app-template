using System.Collections.Concurrent;
using System.Reflection;

namespace AppTemplate.Domain.Common;

public static class ErrorMetadata
{
    private static readonly ConcurrentDictionary<Type, ErrorAttribute> Cache = new();

    public static ErrorAttribute For(Type errorType)
        => Cache.GetOrAdd(errorType, static t =>
            t.GetCustomAttribute<ErrorAttribute>(inherit: false)
            ?? throw new InvalidOperationException($"{t.FullName} must declare [Error(code, kind, title)]"));
}
