using AppTemplate.Domain.Common;
using Shouldly;

namespace AppTemplate.Api.Tests.Domain;

public sealed class AppErrorTests
{
    [Error("test.sample", ErrorKind.Conflict, "Sample title")]
    private sealed record SampleError(string Name) : AppError
    {
        public override string Message => $"m {Name}";
        public override string Suggestion => "s";
    }

    private sealed record Undeclared : AppError
    {
        public override string Message => "";
        public override string Suggestion => "";
    }

    [Fact]
    public void Code_ErrorWithAttribute_ReadsCodeKindTitle()
    {
        var e = new SampleError("x");
        e.Code.ShouldBe("test.sample");
        e.Kind.ShouldBe(ErrorKind.Conflict);
        e.Title.ShouldBe("Sample title");
        e.Message.ShouldBe("m x");
    }

    [Fact]
    public void Code_ErrorWithoutAttribute_ThrowsNamingType()
        => Should.Throw<InvalidOperationException>(() => new Undeclared().Code).Message.ShouldContain(nameof(Undeclared));

    [Fact]
    public void For_FieldError_BuildsSingleFieldFailure()
    {
        var v = ValidationFailed.For("name", new ValidationError("project_name.empty", "Name is empty", "Provide a name"));
        v.Code.ShouldBe("validation.failed");
        v.Fields.ShouldHaveSingleItem().ShouldBe(new FieldFailure("name", "project_name.empty", "Name is empty", "Provide a name"));
    }
}
