using AppTemplate.Api.Actions;
using AppTemplate.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using OneOf;

namespace AppTemplate.Api.Features.Projects;

/// <param name="Status">Only projects in this status; null lists every project.</param>
public sealed record ListProjectsInput(ProjectStatus? Status);

[Query("list_projects", "List projects ordered by name, optionally filtered by status (active or archived).")]
public sealed class ListProjects : IQuery<ListProjectsInput, IReadOnlyList<ProjectSummaryDto>>
{
    public async Task<OneOf<IReadOnlyList<ProjectSummaryDto>>> Run(ListProjectsInput input, ActionContext ctx)
    {
        var projects = ctx.Db.Projects.AsNoTracking();
        if (input.Status is { } status)
        {
            projects = projects.Where(p => p.Status == status);
        }

        return await projects
            .OrderBy(p => p.Name)
            .Select(p => new ProjectSummaryDto(p.Id.Value, p.Name.Value, p.Status, p.Tasks.Count, p.Tasks.Count(t => !t.Done)))
            .ToListAsync(ctx.CancellationToken);
    }
}
