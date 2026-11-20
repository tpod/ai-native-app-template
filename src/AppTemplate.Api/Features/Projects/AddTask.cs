using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;
using AppTemplate.Domain.Projects;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Features.Projects;

public sealed record AddTaskInput(Guid ProjectId, string Title);

[Action("add_task", "Add an open task to an active project.")]
public sealed class AddTask : IMutation<AddTaskInput, Project>
{
    public async Task<OneOf<Project, ProjectNotFound>> Load(AddTaskInput input, ActionContext ctx)
        => await ProjectLookup.Find(input.ProjectId, ctx) is { } project ? project : new ProjectNotFound(ProjectId.From(input.ProjectId));

    public Task<OneOf<Success, ValidationFailed, ProjectIsArchived>> Apply(Project target, AddTaskInput input, ActionContext ctx)
    {
        if (TaskTitle.Create(input.Title).TryPickT1(out var error, out var title))
        {
            return Task.FromResult<OneOf<Success, ValidationFailed, ProjectIsArchived>>(ValidationFailed.For("title", error));
        }

        return Task.FromResult(target.AddTask(TaskItemId.From(ctx.NewId()), title)
            .Match<OneOf<Success, ValidationFailed, ProjectIsArchived>>(success => success, archived => archived));
    }

    public string Summarize(Project target, AddTaskInput input)
        => $"Add task '{input.Title.Trim()}' to project '{target.Name.Value}'.";

    public object ToResult(Project target) => ProjectDto.From(target);
}
