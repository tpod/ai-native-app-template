using AppTemplate.Domain.Common;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Domain.Projects;

public sealed class Project : AggregateRoot<ProjectId>
{
    private readonly List<TaskItem> _tasks = [];

    // EF only.
    private Project() => Name = null!;

    private Project(ProjectId id, ProjectName name)
    {
        Id = id;
        Name = name;
        Status = ProjectStatus.Active;
    }

    public ProjectName Name { get; private set; }

    public ProjectStatus Status { get; private set; }

    public IReadOnlyList<TaskItem> Tasks => _tasks;

    /// <param name="currentHolderOfName">Id of the active project holding this name, or null. A holder equal to <paramref name="id"/> is not a conflict.</param>
    public static OneOf<Project, ProjectNameTaken> Create(ProjectId id, ProjectName name, ProjectId? currentHolderOfName)
    {
        if (currentHolderOfName is { } holder && holder != id)
        {
            return new ProjectNameTaken(name.Value, holder);
        }

        var project = new Project(id, name);
        project.Raise(new ProjectCreated(id.Value, name.Value));
        return project;
    }

    public OneOf<Success, ProjectIsArchived, ProjectNameTaken> Rename(ProjectName name, ProjectId? currentHolderOfName)
    {
        if (Status == ProjectStatus.Archived)
        {
            return new ProjectIsArchived(Id);
        }

        if (string.Equals(name.Value, Name.Value, StringComparison.Ordinal))
        {
            return new Success();
        }

        if (currentHolderOfName is { } holder && holder != Id)
        {
            return new ProjectNameTaken(name.Value, holder);
        }

        var old = Name;
        Name = name;
        Raise(new ProjectRenamed(Id.Value, old.Value, name.Value));
        return new Success();
    }

    public OneOf<Success, ProjectIsArchived> AddTask(TaskItemId id, TaskTitle title)
    {
        if (Status == ProjectStatus.Archived)
        {
            return new ProjectIsArchived(Id);
        }

        _tasks.Add(new TaskItem(id, title));
        Raise(new TaskAdded(Id.Value, id.Value, title.Value));
        return new Success();
    }

    public OneOf<Success, ProjectIsArchived, TaskNotFound, TaskAlreadyDone> CompleteTask(TaskItemId id)
    {
        if (Status == ProjectStatus.Archived)
        {
            return new ProjectIsArchived(Id);
        }

        var task = _tasks.Find(t => t.Id == id);
        if (task is null)
        {
            return new TaskNotFound(Id, id);
        }

        if (task.Done)
        {
            return new TaskAlreadyDone(Id, id);
        }

        task.Complete();
        Raise(new TaskCompleted(Id.Value, id.Value));
        return new Success();
    }

    public OneOf<Success, ProjectIsArchived> Archive()
    {
        if (Status == ProjectStatus.Archived)
        {
            return new ProjectIsArchived(Id);
        }

        Status = ProjectStatus.Archived;
        Raise(new ProjectArchived(Id.Value));
        return new Success();
    }
}
