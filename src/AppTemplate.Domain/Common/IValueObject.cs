namespace AppTemplate.Domain.Common;

public interface IValueObject<TSelf, TValue> where TSelf : IValueObject<TSelf, TValue>
{
    TValue Value { get; }

    /// <summary>For persistence and serialization only; skips validation.</summary>
    static abstract TSelf FromTrusted(TValue value);
}
