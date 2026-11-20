using AppTemplate.Domain.Projects;

namespace AppTemplate.Api.Features.Projects;

/// <summary>A project with its tasks: what every Projects mutation returns and what <c>get_project</c> returns.</summary>
public sealed record ProjectDto(Guid Id, string Name, ProjectStatus Status, int Version, IReadOnlyList<TaskItemDto> Tasks)
{
    public static ProjectDto From(Project project)
        => new(project.Id.Value, project.Name.Value, project.Status, project.Version, [.. project.Tasks.Select(TaskItemDto.From)]);
}

public sealed record TaskItemDto(Guid Id, string Title, bool Done)
{
    public static TaskItemDto From(TaskItem task) => new(task.Id.Value, task.Title.Value, task.Done);
}

/// <summary>One row of <c>list_projects</c>.</summary>
public sealed record ProjectSummaryDto(Guid Id, string Name, ProjectStatus Status, int TaskCount, int OpenTaskCount);
