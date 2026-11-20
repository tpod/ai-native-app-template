using AppTemplate.Api.Actions.Validation;
using AppTemplate.Domain.Projects;
using FluentValidation;

namespace AppTemplate.Api.Features.Projects;

// AddActions registers every public validator in this assembly; the pipeline runs it before Load or Run.

public sealed class CreateProjectInputValidator : AbstractValidator<CreateProjectInput>
{
    public CreateProjectInputValidator() => RuleFor(x => x.Name).MustBeValid(ProjectName.Create);
}

public sealed class RenameProjectInputValidator : AbstractValidator<RenameProjectInput>
{
    public RenameProjectInputValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty().WithSuggestion(ProjectIdSuggestion.Text);
        RuleFor(x => x.Name).MustBeValid(ProjectName.Create);
    }
}

public sealed class AddTaskInputValidator : AbstractValidator<AddTaskInput>
{
    public AddTaskInputValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty().WithSuggestion(ProjectIdSuggestion.Text);
        RuleFor(x => x.Title).MustBeValid(TaskTitle.Create);
    }
}

public sealed class CompleteTaskInputValidator : AbstractValidator<CompleteTaskInput>
{
    public CompleteTaskInputValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty().WithSuggestion(ProjectIdSuggestion.Text);
        RuleFor(x => x.TaskId).NotEmpty().WithSuggestion("Use a task id returned by get_project.");
    }
}

public sealed class ArchiveProjectInputValidator : AbstractValidator<ArchiveProjectInput>
{
    public ArchiveProjectInputValidator() => RuleFor(x => x.ProjectId).NotEmpty().WithSuggestion(ProjectIdSuggestion.Text);
}

public sealed class ListProjectsInputValidator : AbstractValidator<ListProjectsInput>
{
    public ListProjectsInputValidator() => RuleFor(x => x.Status).IsInEnum().WithSuggestion("Use \"active\" or \"archived\", or omit status.");
}

public sealed class GetProjectInputValidator : AbstractValidator<GetProjectInput>
{
    public GetProjectInputValidator() => RuleFor(x => x.ProjectId).NotEmpty().WithSuggestion(ProjectIdSuggestion.Text);
}

internal static class ProjectIdSuggestion
{
    public const string Text = "Use an id returned by list_projects or get_project.";
}
