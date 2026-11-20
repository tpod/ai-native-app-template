using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;
using AppTemplate.Domain.Projects;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Features.Projects;

public sealed record RenameProjectInput(Guid ProjectId, string Name);

[Action("rename_project", "Rename an active project. Names are unique among active projects, ignoring case and surrounding spaces. Warns project.unchanged when the name is the same.")]
public sealed class RenameProject : IMutation<RenameProjectInput, Project>
{
    public async Task<OneOf<Project, ProjectNotFound>> Load(RenameProjectInput input, ActionContext ctx)
        => await ProjectLookup.Find(input.ProjectId, ctx) is { } project ? project : new ProjectNotFound(ProjectId.From(input.ProjectId));

    public async Task<OneOf<Success, ValidationFailed, ProjectIsArchived, ProjectNameTaken>> Apply(
        Project target, RenameProjectInput input, ActionContext ctx)
    {
        if (ProjectName.Create(input.Name).TryPickT1(out var error, out var name))
        {
            return ValidationFailed.For("name", error);
        }

        if (name == target.Name)
        {
            ctx.Warn("project.unchanged", "The new name is the same as the current name; nothing will change.");
        }

        var holder = await ProjectLookup.FindNameHolder(name, ctx);
        return target.Rename(name, holder).Match<OneOf<Success, ValidationFailed, ProjectIsArchived, ProjectNameTaken>>(
            success => success, archived => archived, taken => taken);
    }

    public string Summarize(Project target, RenameProjectInput input)
        => $"Rename project '{target.Name.Value}' to '{input.Name.Trim()}'.";

    public object ToResult(Project target) => ProjectDto.From(target);
}
