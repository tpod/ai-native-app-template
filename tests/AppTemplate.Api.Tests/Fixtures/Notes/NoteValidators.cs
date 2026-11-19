using AppTemplate.Api.Actions.Validation;
using FluentValidation;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed class CreateNoteInputValidator : AbstractValidator<CreateNoteInput>
{
    public CreateNoteInputValidator() => RuleFor(x => x.Text).MustBeValid(NoteText.Create);
}

public sealed class EditNoteInputValidator : AbstractValidator<EditNoteInput>
{
    public EditNoteInputValidator()
    {
        RuleFor(x => x.NoteId).NotEmpty();
        RuleFor(x => x.Text).MustBeValid(NoteText.Create);
    }
}

public sealed class LockNoteInputValidator : AbstractValidator<LockNoteInput>
{
    public LockNoteInputValidator() => RuleFor(x => x.NoteId).NotEmpty();
}

public sealed class GetNoteInputValidator : AbstractValidator<GetNoteInput>
{
    public GetNoteInputValidator() => RuleFor(x => x.NoteId).NotEmpty();
}
