using AppTemplate.Api.Actions;
using AppTemplate.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Features.Projects;

/// <summary>Database reads shared by the Projects mutations.</summary>
internal static class ProjectLookup
{
    /// <summary>The tracked project with this id, tasks included, or null.</summary>
    public static Task<Project?> Find(Guid id, ActionContext ctx)
    {
        var projectId = ProjectId.From(id);
        return ctx.Db.Projects.Include(p => p.Tasks).FirstOrDefaultAsync(p => p.Id == projectId, ctx.CancellationToken);
    }

    /// <summary>
    /// The id of the active project that holds this name, or null. The column's NOCASE collation makes the
    /// comparison case-insensitive, and <see cref="ProjectName"/> is already trimmed.
    /// </summary>
    public static Task<ProjectId?> FindNameHolder(ProjectName name, ActionContext ctx)
        => ctx.Db.Projects
            .Where(p => p.Status == ProjectStatus.Active && p.Name == name)
            .Select(p => (ProjectId?)p.Id)
            .FirstOrDefaultAsync(ctx.CancellationToken);
}
