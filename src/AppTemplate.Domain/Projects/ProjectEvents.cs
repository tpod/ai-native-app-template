using AppTemplate.Domain.Common;

namespace AppTemplate.Domain.Projects;

public sealed record ProjectCreated(Guid ProjectId, string Name) : IDomainEvent;

public sealed record ProjectRenamed(Guid ProjectId, string OldName, string NewName) : IDomainEvent;

public sealed record TaskAdded(Guid ProjectId, Guid TaskId, string Title) : IDomainEvent;

public sealed record TaskCompleted(Guid ProjectId, Guid TaskId) : IDomainEvent;

public sealed record ProjectArchived(Guid ProjectId) : IDomainEvent;
