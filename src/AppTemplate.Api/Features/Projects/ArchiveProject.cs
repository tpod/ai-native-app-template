using AppTemplate.Api.Actions;
using AppTemplate.Domain.Projects;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Features.Projects;

public sealed record ArchiveProjectInput(Guid ProjectId);

[Action("archive_project", "Archive a project. An archived project is read-only, and its name becomes free for a new project.", Destructive = true)]
public sealed class ArchiveProject : IMutation<ArchiveProjectInput, Project>
{
    public async Task<OneOf<Project, ProjectNotFound>> Load(ArchiveProjectInput input, ActionContext ctx)
        => await ProjectLookup.Find(input.ProjectId, ctx) is { } project ? project : new ProjectNotFound(ProjectId.From(input.ProjectId));

    public Task<OneOf<Success, ProjectIsArchived>> Apply(Project target, ArchiveProjectInput input, ActionContext ctx)
        => Task.FromResult(target.Archive());

    public string Summarize(Project target, ArchiveProjectInput input) => $"Archive project '{target.Name.Value}'.";

    public object ToResult(Project target) => ProjectDto.From(target);
}
