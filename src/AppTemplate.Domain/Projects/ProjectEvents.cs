using AppTemplate.Domain.Common;

namespace AppTemplate.Domain.Projects;

[Event("project.created", 1)]
public sealed record ProjectCreated(Guid ProjectId, string Name) : IDomainEvent;

[Event("project.renamed", 1)]
public sealed record ProjectRenamed(Guid ProjectId, string OldName, string NewName) : IDomainEvent;

[Event("project.task_added", 1)]
public sealed record TaskAdded(Guid ProjectId, Guid TaskId, string Title) : IDomainEvent;

[Event("project.task_completed", 1)]
public sealed record TaskCompleted(Guid ProjectId, Guid TaskId) : IDomainEvent;

[Event("project.archived", 1)]
public sealed record ProjectArchived(Guid ProjectId) : IDomainEvent;
