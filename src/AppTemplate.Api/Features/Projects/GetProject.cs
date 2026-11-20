using AppTemplate.Api.Actions;
using AppTemplate.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using OneOf;

namespace AppTemplate.Api.Features.Projects;

public sealed record GetProjectInput(Guid ProjectId);

[Query("get_project", "Get one project with its tasks.")]
public sealed class GetProject : IQuery<GetProjectInput, ProjectDto>
{
    public async Task<OneOf<ProjectDto, ProjectNotFound>> Run(GetProjectInput input, ActionContext ctx)
    {
        var id = ProjectId.From(input.ProjectId);
        var project = await ctx.Db.Projects.AsNoTracking().Include(p => p.Tasks).FirstOrDefaultAsync(p => p.Id == id, ctx.CancellationToken);
        return project is null ? new ProjectNotFound(id) : ProjectDto.From(project);
    }
}
