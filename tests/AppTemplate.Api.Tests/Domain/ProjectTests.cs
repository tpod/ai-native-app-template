using AppTemplate.Domain.Projects;
using Shouldly;

namespace AppTemplate.Api.Tests.Domain;

public sealed class ProjectTests
{
    private static ProjectName Name(string v) => ProjectName.Create(v).AsT0;
    private static TaskTitle Title(string v) => TaskTitle.Create(v).AsT0;
    private static ProjectId NewId() => ProjectId.From(Guid.NewGuid());

    private static Project NewProject(string name = "Alpha")
    {
        var p = Project.Create(NewId(), Name(name), null).AsT0;
        p.ClearDomainEvents();
        return p;
    }

    [Fact]
    public void Create_NameFree_RaisesProjectCreated()
    {
        var id = NewId();
        var result = Project.Create(id, Name("Alpha"), null);

        result.IsT0.ShouldBeTrue();
        var p = result.AsT0;
        p.Id.ShouldBe(id);
        p.Name.Value.ShouldBe("Alpha");
        p.Status.ShouldBe(ProjectStatus.Active);
        var e = p.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProjectCreated>();
        e.ProjectId.ShouldBe(id.Value);
        e.Name.ShouldBe("Alpha");
    }

    [Fact]
    public void Create_NameHeldByOther_ReturnsNameTaken()
    {
        var holder = NewId();
        var result = Project.Create(NewId(), Name("Alpha"), holder);

        result.IsT1.ShouldBeTrue();
        var err = result.AsT1.ShouldBeOfType<ProjectNameTaken>();
        err.ExistingProjectId.ShouldBe(holder);
        err.Name.ShouldBe("Alpha");
    }

    [Fact]
    public void Rename_SameName_SucceedsWithoutEvent()
    {
        var p = NewProject("Alpha");
        var result = p.Rename(Name("  Alpha "), null);

        result.IsT0.ShouldBeTrue();
        p.DomainEvents.ShouldBeEmpty();
        p.Name.Value.ShouldBe("Alpha");
    }

    [Fact]
    public void Rename_NameHeldBySelf_Succeeds()
    {
        var p = NewProject("Alpha");
        var result = p.Rename(Name("Beta"), p.Id);

        result.IsT0.ShouldBeTrue();
        p.Name.Value.ShouldBe("Beta");
    }

    [Fact]
    public void Rename_NameHeldByOther_ReturnsNameTaken()
    {
        var p = NewProject("Alpha");
        var holder = NewId();
        var result = p.Rename(Name("Beta"), holder);

        result.IsT2.ShouldBeTrue();
        result.AsT2.ExistingProjectId.ShouldBe(holder);
        p.Name.Value.ShouldBe("Alpha");
    }

    [Fact]
    public void Rename_Archived_ReturnsArchived()
    {
        var p = NewProject();
        p.Archive();
        var result = p.Rename(Name("Beta"), null);

        result.IsT1.ShouldBeTrue();
        result.AsT1.ShouldBeOfType<ProjectIsArchived>().ProjectId.ShouldBe(p.Id);
    }

    [Fact]
    public void Rename_NewName_RaisesProjectRenamed()
    {
        var p = NewProject("Alpha");
        var result = p.Rename(Name("Beta"), null);

        result.IsT0.ShouldBeTrue();
        p.Name.Value.ShouldBe("Beta");
        var e = p.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProjectRenamed>();
        e.ProjectId.ShouldBe(p.Id.Value);
        e.OldName.ShouldBe("Alpha");
        e.NewName.ShouldBe("Beta");
    }

    [Fact]
    public void AddTask_Active_AddsTaskAndRaisesTaskAdded()
    {
        var p = NewProject();
        var taskId = TaskItemId.From(Guid.NewGuid());
        var result = p.AddTask(taskId, Title("Do it"));

        result.IsT0.ShouldBeTrue();
        var t = p.Tasks.ShouldHaveSingleItem();
        t.Id.ShouldBe(taskId);
        t.Title.Value.ShouldBe("Do it");
        t.Done.ShouldBeFalse();
        var e = p.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TaskAdded>();
        e.ProjectId.ShouldBe(p.Id.Value);
        e.TaskId.ShouldBe(taskId.Value);
        e.Title.ShouldBe("Do it");
    }

    [Fact]
    public void AddTask_Archived_ReturnsArchived()
    {
        var p = NewProject();
        p.Archive();
        var result = p.AddTask(TaskItemId.From(Guid.NewGuid()), Title("Do it"));

        result.IsT1.ShouldBeTrue();
        result.AsT1.ShouldBeOfType<ProjectIsArchived>();
        p.Tasks.ShouldBeEmpty();
    }

    [Fact]
    public void CompleteTask_Existing_MarksDoneAndRaisesTaskCompleted()
    {
        var p = NewProject();
        var taskId = TaskItemId.From(Guid.NewGuid());
        p.AddTask(taskId, Title("Do it"));
        p.ClearDomainEvents();

        var result = p.CompleteTask(taskId);

        result.IsT0.ShouldBeTrue();
        p.Tasks.ShouldHaveSingleItem().Done.ShouldBeTrue();
        var e = p.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TaskCompleted>();
        e.ProjectId.ShouldBe(p.Id.Value);
        e.TaskId.ShouldBe(taskId.Value);
    }

    [Fact]
    public void CompleteTask_Archived_ReturnsArchived()
    {
        var p = NewProject();
        var taskId = TaskItemId.From(Guid.NewGuid());
        p.AddTask(taskId, Title("Do it"));
        p.Archive();

        var result = p.CompleteTask(taskId);

        result.IsT1.ShouldBeTrue();
        result.AsT1.ShouldBeOfType<ProjectIsArchived>();
    }

    [Fact]
    public void CompleteTask_Unknown_ReturnsTaskNotFound()
    {
        var p = NewProject();
        var unknown = TaskItemId.From(Guid.NewGuid());
        var result = p.CompleteTask(unknown);

        result.IsT2.ShouldBeTrue();
        var err = result.AsT2.ShouldBeOfType<TaskNotFound>();
        err.ProjectId.ShouldBe(p.Id);
        err.TaskId.ShouldBe(unknown);
    }

    [Fact]
    public void CompleteTask_Twice_ReturnsAlreadyDone()
    {
        var p = NewProject();
        var taskId = TaskItemId.From(Guid.NewGuid());
        p.AddTask(taskId, Title("Do it"));
        p.CompleteTask(taskId);
        p.ClearDomainEvents();

        var result = p.CompleteTask(taskId);

        result.IsT3.ShouldBeTrue();
        result.AsT3.ShouldBeOfType<TaskAlreadyDone>().TaskId.ShouldBe(taskId);
        p.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Archive_Twice_ReturnsArchived()
    {
        var p = NewProject();
        p.Archive();
        var result = p.Archive();

        result.IsT1.ShouldBeTrue();
        result.AsT1.ShouldBeOfType<ProjectIsArchived>();
    }

    [Fact]
    public void Archive_Active_RaisesProjectArchived()
    {
        var p = NewProject();
        var result = p.Archive();

        result.IsT0.ShouldBeTrue();
        p.Status.ShouldBe(ProjectStatus.Archived);
        p.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProjectArchived>().ProjectId.ShouldBe(p.Id.Value);
    }

    [Fact]
    public void Errors_HaveExpectedCodesAndKinds()
    {
        var id = NewId();
        var tid = TaskItemId.From(Guid.NewGuid());
        (new ProjectNotFound(id).Code, new ProjectNotFound(id).Kind).ShouldBe(("project.not_found", AppTemplate.Domain.Common.ErrorKind.NotFound));
        (new ProjectNameTaken("a", id).Code, new ProjectNameTaken("a", id).Kind).ShouldBe(("project.name_taken", AppTemplate.Domain.Common.ErrorKind.Conflict));
        (new ProjectIsArchived(id).Code, new ProjectIsArchived(id).Kind).ShouldBe(("project.archived", AppTemplate.Domain.Common.ErrorKind.Conflict));
        (new TaskNotFound(id, tid).Code, new TaskNotFound(id, tid).Kind).ShouldBe(("task.not_found", AppTemplate.Domain.Common.ErrorKind.NotFound));
        (new TaskAlreadyDone(id, tid).Code, new TaskAlreadyDone(id, tid).Kind).ShouldBe(("task.already_done", AppTemplate.Domain.Common.ErrorKind.Conflict));
    }
}
