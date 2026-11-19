using AppTemplate.Domain.Common;

namespace AppTemplate.Domain.Projects;

[Error("project.not_found", ErrorKind.NotFound, "Project not found")]
public sealed record ProjectNotFound(ProjectId ProjectId) : AppError
{
    public override string Message => $"Project {ProjectId.Value} does not exist.";
    public override string Suggestion => "Check the id; list_projects returns valid project ids.";
    public override object? Details => new { projectId = ProjectId.Value };
}

[Error("project.name_taken", ErrorKind.Conflict, "Project name already taken")]
public sealed record ProjectNameTaken(string Name, ProjectId ExistingProjectId) : AppError
{
    public override string Message => $"An active project named '{Name}' already exists.";
    public override string Suggestion => "Choose a different name; list_projects shows existing names.";
    public override object? Details => new { name = Name, existingProjectId = ExistingProjectId.Value };
}

[Error("project.archived", ErrorKind.Conflict, "Project is archived")]
public sealed record ProjectIsArchived(ProjectId ProjectId) : AppError
{
    public override string Message => $"Project {ProjectId.Value} is archived and cannot be changed.";
    public override string Suggestion => "Create a new project instead; archived projects are read-only.";
    public override object? Details => new { projectId = ProjectId.Value };
}

[Error("task.not_found", ErrorKind.NotFound, "Task not found")]
public sealed record TaskNotFound(ProjectId ProjectId, TaskItemId TaskId) : AppError
{
    public override string Message => $"Task {TaskId.Value} does not exist in project {ProjectId.Value}.";
    public override string Suggestion => "Check the ids; get_project lists the project's tasks.";
    public override object? Details => new { projectId = ProjectId.Value, taskId = TaskId.Value };
}

[Error("task.already_done", ErrorKind.Conflict, "Task already done")]
public sealed record TaskAlreadyDone(ProjectId ProjectId, TaskItemId TaskId) : AppError
{
    public override string Message => $"Task {TaskId.Value} in project {ProjectId.Value} is already done.";
    public override string Suggestion => "No action needed; get_project shows each task's state.";
    public override object? Details => new { projectId = ProjectId.Value, taskId = TaskId.Value };
}
