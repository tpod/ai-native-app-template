using AppTemplate.Domain.Common;

namespace AppTemplate.Domain.Projects;

public readonly record struct ProjectId : IValueObject<ProjectId, Guid>
{
    private ProjectId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ProjectId From(Guid value) => new(value);

    public static ProjectId FromTrusted(Guid value) => new(value);
}
