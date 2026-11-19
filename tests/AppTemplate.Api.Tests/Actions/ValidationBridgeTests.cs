using AppTemplate.Api.Actions.Validation;
using AppTemplate.Domain.Projects;
using FluentValidation;
using Shouldly;

namespace AppTemplate.Api.Tests.Actions;

public sealed class ValidationBridgeTests
{
    private sealed record SampleInput(Guid ProjectId, string Name);

    private sealed record Line(string Name);

    private sealed record Order(IReadOnlyList<Line> Items, string URLPath);

    private sealed class SampleValidator : AbstractValidator<SampleInput>
    {
        public SampleValidator()
        {
            RuleFor(x => x.ProjectId).NotEmpty().WithSuggestion("Pass an id from list_projects.");
            RuleFor(x => x.Name).MustBeValid(ProjectName.Create);
        }
    }

    private sealed class NoSuggestionValidator : AbstractValidator<SampleInput>
    {
        public NoSuggestionValidator() => RuleFor(x => x.ProjectId).NotEmpty();
    }

    private sealed class LineValidator : AbstractValidator<Line>
    {
        public LineValidator() => RuleFor(x => x.Name).MustBeValid(ProjectName.Create);
    }

    private sealed class OrderValidator : AbstractValidator<Order>
    {
        public OrderValidator()
        {
            RuleForEach(x => x.Items).SetValidator(new LineValidator());
            RuleFor(x => x.URLPath).NotEmpty();
        }
    }

    [Fact]
    public async Task From_BothFieldsInvalid_ReturnsAllFailures()
    {
        var f = ValidationFailedMapper.From(await new SampleValidator().ValidateAsync(new SampleInput(Guid.Empty, "   "), TestContext.Current.CancellationToken));

        f.Fields.Count.ShouldBe(2);
        f.Fields.ShouldContain(x => x.Field == "projectId" && x.Code == "project_id.not_empty" && x.Suggestion == "Pass an id from list_projects.");
        f.Fields.ShouldContain(x => x.Field == "name" && x.Code == "project_name.empty" && x.Message == "Project name is empty." && x.Suggestion == "Provide a non-blank project name.");
    }

    [Fact]
    public async Task MustBeValid_ValidName_NoFailures()
    {
        var result = await new SampleValidator().ValidateAsync(new SampleInput(Guid.NewGuid(), "Alpha"), TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task From_RuleWithoutSuggestion_UsesDefaultSuggestion()
    {
        var f = ValidationFailedMapper.From(await new NoSuggestionValidator().ValidateAsync(new SampleInput(Guid.Empty, "x"), TestContext.Current.CancellationToken));

        f.Fields.Count.ShouldBe(1);
        f.Fields[0].Suggestion.ShouldBe("Provide a valid value for projectId.");
    }

    [Fact]
    public async Task From_NestedCollectionAndAcronym_CamelCasesPathsAndSnakeCasesCodes()
    {
        var f = ValidationFailedMapper.From(await new OrderValidator().ValidateAsync(new Order([new Line("ok"), new Line(" ")], ""), TestContext.Current.CancellationToken));

        f.Fields.ShouldContain(x => x.Field == "items[1].name" && x.Code == "project_name.empty");
        f.Fields.ShouldContain(x => x.Field == "urlPath" && x.Code == "url_path.not_empty");
    }
}
