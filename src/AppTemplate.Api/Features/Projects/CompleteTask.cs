using AppTemplate.Api.Actions;
using AppTemplate.Domain.Projects;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Features.Projects;

public sealed record CompleteTaskInput(Guid ProjectId, Guid TaskId);

[Action("complete_task", "Mark an open task of an active project as done.")]
public sealed class CompleteTask : IMutation<CompleteTaskInput, Project>
{
    public async Task<OneOf<Project, ProjectNotFound>> Load(CompleteTaskInput input, ActionContext ctx)
        => await ProjectLookup.Find(input.ProjectId, ctx) is { } project ? project : new ProjectNotFound(ProjectId.From(input.ProjectId));

    public Task<OneOf<Success, ProjectIsArchived, TaskNotFound, TaskAlreadyDone>> Apply(
        Project target, CompleteTaskInput input, ActionContext ctx)
        => Task.FromResult(target.CompleteTask(TaskItemId.From(input.TaskId)));

    public string Summarize(Project target, CompleteTaskInput input)
    {
        var taskId = TaskItemId.From(input.TaskId);
        var task = target.Tasks.FirstOrDefault(t => t.Id == taskId);

        // Summarize runs before Apply, so the task may not exist; Apply then fails with task.not_found.
        return task is null
            ? $"Complete task {input.TaskId} in project '{target.Name.Value}'."
            : $"Complete task '{task.Title.Value}' in project '{target.Name.Value}'.";
    }

    public object ToResult(Project target) => ProjectDto.From(target);
}
