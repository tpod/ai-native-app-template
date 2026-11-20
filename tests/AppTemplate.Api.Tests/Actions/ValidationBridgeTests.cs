using AppTemplate.Api.Actions.Validation;
using AppTemplate.Api.Tests.Fixtures.Notes;
using FluentValidation;
using Shouldly;

namespace AppTemplate.Api.Tests.Actions;

public sealed class ValidationBridgeTests
{
    private sealed record SampleInput(Guid NoteId, string Text);

    private sealed record Line(string Text);

    private sealed record Order(IReadOnlyList<Line> Items, string URLPath);

    private sealed class SampleValidator : AbstractValidator<SampleInput>
    {
        public SampleValidator()
        {
            RuleFor(x => x.NoteId).NotEmpty().WithSuggestion("Pass an id from fixture_get_note.");
            RuleFor(x => x.Text).MustBeValid(NoteText.Create);
        }
    }

    private sealed class NoSuggestionValidator : AbstractValidator<SampleInput>
    {
        public NoSuggestionValidator() => RuleFor(x => x.NoteId).NotEmpty();
    }

    private sealed class LineValidator : AbstractValidator<Line>
    {
        public LineValidator() => RuleFor(x => x.Text).MustBeValid(NoteText.Create);
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
        f.Fields.ShouldContain(x => x.Field == "noteId" && x.Code == "note_id.not_empty" && x.Suggestion == "Pass an id from fixture_get_note.");
        f.Fields.ShouldContain(x => x.Field == "text" && x.Code == "note_text.empty" && x.Message == "Note text is empty." && x.Suggestion == "Provide non-blank note text.");
    }

    [Fact]
    public async Task MustBeValid_ValidText_NoFailures()
    {
        var result = await new SampleValidator().ValidateAsync(new SampleInput(Guid.NewGuid(), "hello"), TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task From_RuleWithoutSuggestion_UsesDefaultSuggestion()
    {
        var f = ValidationFailedMapper.From(await new NoSuggestionValidator().ValidateAsync(new SampleInput(Guid.Empty, "x"), TestContext.Current.CancellationToken));

        f.Fields.Count.ShouldBe(1);
        f.Fields[0].Suggestion.ShouldBe("Provide a valid value for noteId.");
    }

    [Fact]
    public async Task From_NestedCollectionAndAcronym_CamelCasesPathsAndSnakeCasesCodes()
    {
        var f = ValidationFailedMapper.From(await new OrderValidator().ValidateAsync(new Order([new Line("ok"), new Line(" ")], ""), TestContext.Current.CancellationToken));

        f.Fields.ShouldContain(x => x.Field == "items[1].text" && x.Code == "note_text.empty");
        f.Fields.ShouldContain(x => x.Field == "urlPath" && x.Code == "url_path.not_empty");
    }
}
