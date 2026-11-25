using AppTemplate.Api.Actions;
using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

/// <summary>Registers the Notes test fixture: handlers, validators, the event recorder and the model contributor.</summary>
public static class NoteFixture
{
    /// <remarks>
    /// Handlers are added through <see cref="ActionRegistryOptions.Types"/> and validators by type. Never add this test
    /// assembly to <see cref="ActionRegistryOptions.Assemblies"/>: it also holds deliberately invalid handlers.
    /// </remarks>
    public static void Register(IServiceCollection services)
    {
        services.Configure<ActionRegistryOptions>(o =>
            o.Types.AddRange([typeof(CreateNote), typeof(EditNote), typeof(LockNote), typeof(Explode), typeof(GetNote), typeof(ListNotes), typeof(UntrackedEditNote)]));

        services.AddScoped<IValidator<CreateNoteInput>, CreateNoteInputValidator>();
        services.AddScoped<IValidator<EditNoteInput>, EditNoteInputValidator>();
        services.AddScoped<IValidator<LockNoteInput>, LockNoteInputValidator>();
        services.AddScoped<IValidator<GetNoteInput>, GetNoteInputValidator>();

        services.AddEventHandler<RecordingEventHandler>(ServiceLifetime.Singleton);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelContributor, NoteModelContributor>());
    }

    /// <summary>The tracked note with this id, or null.</summary>
    internal static Task<Note?> FindNote(Guid id, ActionContext ctx)
    {
        var noteId = NoteId.From(id);
        return ctx.Db.Set<Note>().FirstOrDefaultAsync(n => n.Id == noteId, ctx.CancellationToken);
    }
}
