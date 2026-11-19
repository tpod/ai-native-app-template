using AppTemplate.Domain.Common;

namespace AppTemplate.Domain.Projects;

public readonly record struct TaskItemId : IValueObject<TaskItemId, Guid>
{
    private TaskItemId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TaskItemId From(Guid value) => new(value);

    public static TaskItemId FromTrusted(Guid value) => new(value);
}
