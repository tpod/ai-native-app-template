using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;
using AppTemplate.Domain.Projects;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Features.Projects;

public sealed record CreateProjectInput(string Name);

[Action("create_project", "Create an active project. Names are unique among active projects, ignoring case and surrounding spaces.")]
public sealed class CreateProject : IMutation<CreateProjectInput, Project>
{
    public async Task<OneOf<Project, ProjectNameTaken, ValidationFailed>> Load(CreateProjectInput input, ActionContext ctx)
    {
        if (ProjectName.Create(input.Name).TryPickT1(out var error, out var name))
        {
            return ValidationFailed.For("name", error);
        }

        var holder = await ProjectLookup.FindNameHolder(name, ctx);
        if (Project.Create(ProjectId.From(ctx.NewId()), name, holder).TryPickT1(out var taken, out var project))
        {
            return taken;
        }

        ctx.Db.Projects.Add(project);
        return project;
    }

    public Task<OneOf<Success>> Apply(Project target, CreateProjectInput input, ActionContext ctx)
        => Task.FromResult<OneOf<Success>>(new Success());

    public string Summarize(Project target, CreateProjectInput input) => $"Create project '{target.Name.Value}'.";

    public object ToResult(Project target) => ProjectDto.From(target);
}
