using AppTemplate.Api.Actions;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed record ExplodeInput;

/// <summary>Throws from Apply, to prove exception text never reaches a response.</summary>
[Action("fixture_explode", "Test fixture: always throws from Apply.")]
public sealed class Explode : IMutation<ExplodeInput, Note>
{
    public Task<OneOf<Note>> Load(ExplodeInput input, ActionContext ctx)
    {
        var note = Note.Create(NoteId.From(ctx.NewId()), NoteText.Create("explode").AsT0);
        ctx.Db.Add(note);
        return Task.FromResult<OneOf<Note>>(note);
    }

    public Task<OneOf<Success>> Apply(Note target, ExplodeInput input, ActionContext ctx)
        => throw new InvalidOperationException("boom-secret");

    public string Summarize(Note target, ExplodeInput input) => "Explode";

    public object ToResult(Note target) => NoteDto.From(target);
}
