using AppTemplate.Domain.Common;

namespace AppTemplate.Domain.Projects;

public sealed class TaskItem : Entity<TaskItemId>
{
    // EF only.
    private TaskItem() => Title = null!;

    internal TaskItem(TaskItemId id, TaskTitle title)
    {
        Id = id;
        Title = title;
    }

    public TaskTitle Title { get; private set; }

    public bool Done { get; private set; }

    internal void Complete() => Done = true;
}
