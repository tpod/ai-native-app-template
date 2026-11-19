using System.Text.Json;
using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Actions.Preview;
using AppTemplate.Api.Actions.Validation;
using AppTemplate.Api.Data;
using AppTemplate.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OneOf;

namespace AppTemplate.Api.Actions;

/// <summary>
/// Runs preview, execute and query for every registered action (spec §7.4). HTTP and MCP are thin adapters over it.
/// </summary>
/// <remarks>
/// <para>
/// Each call opens its own DI scope, so it gets a fresh <see cref="AppDbContext"/> and fresh handler instance.
/// Every call first deserializes the body into the input record (<c>request.malformed</c> on failure) and runs the
/// input's <c>IValidator&lt;TInput&gt;</c> when one is registered (<c>validation.failed</c>).
/// </para>
/// <para>
/// Handler exceptions are not caught: they propagate to the host's exception handler, which answers
/// <c>internal.unexpected</c> without exception text.
/// </para>
/// </remarks>
public sealed partial class ActionPipeline(
    IServiceScopeFactory scopes,
    PreviewTokenSigner signer,
    TimeProvider time,
    IOptions<JsonOptions> jsonOptions,
    ILogger<ActionPipeline> logger)
{
    /// <summary>Stale reasons reported in <c>details.reason</c> of <c>preview.stale</c>.</summary>
    public static class StaleReasons
    {
        public const string VersionChanged = "version_changed";
        public const string DomainError = "domain_error";
        public const string ChangesDiffer = "changes_differ";
        public const string ConcurrentWrite = "concurrent_write";
    }

    private JsonSerializerOptions Json => jsonOptions.Value.SerializerOptions;

    /// <summary>
    /// Load, Summarize and Apply without saving; returns the change set and a signed token that binds it.
    /// Domain events are reported as side effects, then cleared; a preview never dispatches them.
    /// </summary>
    public async Task<OneOf<PreviewResponse, AppError>> PreviewAsync(ActionDescriptor action, JsonElement body, CancellationToken ct)
    {
        RequireKind(action, ActionKind.Mutation);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var parsed = await ReadInputAsync(action, body, services, ct);
        if (parsed.TryPickT1(out var invalid, out var input))
        {
            return invalid;
        }

        var db = services.GetRequiredService<AppDbContext>();
        var ctx = new ActionContext(db, time, replayIds: null, ct);
        var handler = ActivatorUtilities.GetServiceOrCreateInstance(services, action.HandlerType);

        var loaded = await action.LoadAsync!(handler, input, ctx);
        if (loaded.Index != 0)
        {
            return (AppError)loaded.Value;
        }

        var target = loaded.Value;
        var versions = AggregateVersions(db);
        var summary = action.Summarize!(handler, target, input);

        var applied = await action.ApplyAsync!(handler, target, input, ctx);
        if (applied.Index != 0)
        {
            return (AppError)applied.Value;
        }

        var (changes, sideEffects, _) = ReadChangeSet(db);
        IReadOnlyList<PreviewWarning> warnings = [.. ctx.Warnings];
        var expiresAt = time.GetUtcNow() + action.PreviewLifetime;
        var token = signer.Sign(new PreviewTokenPayload(
            action.Name,
            InputHasher.Hash(input, action.InputType, Json),
            versions,
            ChangeSetHasher.Hash(changes, sideEffects, warnings),
            [.. ctx.AllocatedIds],
            expiresAt));

        // The scope is disposed without SaveChanges, so nothing is written.
        return new PreviewResponse(action.Name, summary, changes, sideEffects, warnings, token, expiresAt);
    }

    /// <summary>
    /// Re-runs the previewed action and commits it only if it still produces exactly the previewed change set.
    /// The body holds the input fields plus <c>previewToken</c>.
    /// </summary>
    /// <remarks>
    /// Order: token signature, action and input hash, expiry, Load (replaying allocated ids), already-executed check,
    /// version check, Apply, change-set hash, save (skipped with the version bump when nothing changed), event dispatch,
    /// <c>ToResult</c>. Events are dispatched in the execute scope after the commit; a handler exception then surfaces
    /// as a 500 although the change is already saved (no outbox, by design).
    /// </remarks>
    public async Task<OneOf<ExecuteResponse, AppError>> ExecuteAsync(ActionDescriptor action, JsonElement body, CancellationToken ct)
    {
        RequireKind(action, ActionKind.Mutation);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var parsed = await ReadInputAsync(action, body, services, ct);
        if (parsed.TryPickT1(out var invalid, out var input))
        {
            return invalid;
        }

        var tokenText = body.TryGetProperty("previewToken", out var tokenElement) && tokenElement.ValueKind == JsonValueKind.String
            ? tokenElement.GetString()
            : null;
        if (string.IsNullOrEmpty(tokenText) || signer.Verify(tokenText).TryPickT1(out _, out var token))
        {
            return new PreviewTokenInvalid();
        }

        if (!string.Equals(token.Action, action.Name, StringComparison.Ordinal))
        {
            return new PreviewTokenMismatch(token.Action, action.Name);
        }

        if (!string.Equals(token.InputHash, InputHasher.Hash(input, action.InputType, Json), StringComparison.Ordinal))
        {
            return new PreviewTokenMismatch($"{action.Name} with the previewed input", $"{action.Name} with different input");
        }

        if (time.GetUtcNow() >= token.ExpiresAt)
        {
            return new PreviewExpired(token.ExpiresAt);
        }

        var db = services.GetRequiredService<AppDbContext>();
        var ctx = new ActionContext(db, time, token.AllocatedIds, ct);
        var handler = ActivatorUtilities.GetServiceOrCreateInstance(services, action.HandlerType);

        var loaded = await action.LoadAsync!(handler, input, ctx);
        if (await AnyCreatedAggregateExistsAsync(db, ct))
        {
            return new PreviewAlreadyExecuted(action.Name);
        }

        if (loaded.Index != 0)
        {
            return Stale(action, StaleReasons.DomainError, CauseOf((AppError)loaded.Value));
        }

        var target = loaded.Value;
        if (!AggregateVersions(db).SequenceEqual(token.AggregateVersions))
        {
            return Stale(action, StaleReasons.VersionChanged, cause: null);
        }

        var applied = await action.ApplyAsync!(handler, target, input, ctx);
        if (applied.Index != 0)
        {
            return Stale(action, StaleReasons.DomainError, CauseOf((AppError)applied.Value));
        }

        var (changes, sideEffects, events) = ReadChangeSet(db);
        if (!string.Equals(ChangeSetHasher.Hash(changes, sideEffects, ctx.Warnings), token.ChangesHash, StringComparison.Ordinal))
        {
            return Stale(action, StaleReasons.ChangesDiffer, cause: null);
        }

        // A no-op keeps Version as it is, so other open previews of the same aggregate stay valid.
        if (changes.Count > 0)
        {
            ((IAggregateRoot)target).IncrementVersion();
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Stale(action, StaleReasons.ConcurrentWrite, cause: null);
            }
            catch (DbUpdateException)
            {
                // Two executes of one create token racing past the check above: the loser hits the key constraint.
                if (await AnyCreatedAggregateExistsAsync(db, ct))
                {
                    return new PreviewAlreadyExecuted(action.Name);
                }

                throw;
            }
        }

        await services.GetRequiredService<DomainEventDispatcher>().DispatchAsync(events, ct);

        return new ExecuteResponse(action.Name, action.ToResult!(handler, target), changes);
    }

    /// <summary>Runs a query in its own scope and returns its output.</summary>
    public async Task<OneOf<object, AppError>> QueryAsync(ActionDescriptor action, JsonElement body, CancellationToken ct)
    {
        RequireKind(action, ActionKind.Query);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var parsed = await ReadInputAsync(action, body, services, ct);
        if (parsed.TryPickT1(out var invalid, out var input))
        {
            return invalid;
        }

        var ctx = new ActionContext(services.GetRequiredService<AppDbContext>(), time, replayIds: null, ct);
        var handler = ActivatorUtilities.GetServiceOrCreateInstance(services, action.HandlerType);
        var result = await action.RunAsync!(handler, input, ctx);
        return result.Index == 0 ? OneOf<object, AppError>.FromT0(result.Value) : (AppError)result.Value;
    }

    private async Task<OneOf<object, AppError>> ReadInputAsync(
        ActionDescriptor action, JsonElement body, IServiceProvider services, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return new RequestMalformed($"The request body must be a JSON object holding the input of '{action.Name}'.", "$");
        }

        object? input;
        try
        {
            input = body.Deserialize(action.InputType, Json);
        }
        catch (JsonException e)
        {
            var path = e.Path ?? "$";
            return new RequestMalformed($"The request body does not match the input of '{action.Name}' at '{path}'.", path);
        }

        if (input is null)
        {
            return new RequestMalformed($"The request body must be a JSON object holding the input of '{action.Name}'.", "$");
        }

        if (services.GetService(typeof(IValidator<>).MakeGenericType(action.InputType)) is IValidator validator)
        {
            var result = await validator.ValidateAsync(new ValidationContext<object>(input), ct);
            if (!result.IsValid)
            {
                return ValidationFailedMapper.From(result);
            }
        }

        return input;
    }

    /// <summary>(type, id, Version) of every tracked aggregate root that already exists in the database, sorted.</summary>
    private static List<AggregateVersion> AggregateVersions(AppDbContext db)
        => [.. db.ChangeTracker.Entries<IAggregateRoot>()
            .Where(e => e.State is EntityState.Unchanged or EntityState.Modified)
            .Select(e => new AggregateVersion(e.Metadata.ClrType.Name, ChangeSetReader.KeyOf(e), e.Entity.Version))
            .OrderBy(v => v.Type, StringComparer.Ordinal)
            .ThenBy(v => v.Id, StringComparer.Ordinal)];

    /// <summary>True when an aggregate the handler added (with a replayed id) is already in the database.</summary>
    private static async Task<bool> AnyCreatedAggregateExistsAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var entry in db.ChangeTracker.Entries<IAggregateRoot>().Where(e => e.State == EntityState.Added).ToList())
        {
            if (await entry.GetDatabaseValuesAsync(ct) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Pending changes, the raised domain events as side effects, and the events themselves (then cleared).</summary>
    private (IReadOnlyList<Change> Changes, IReadOnlyList<SideEffect> SideEffects, IReadOnlyList<IDomainEvent> Events) ReadChangeSet(AppDbContext db)
    {
        var changes = ChangeSetReader.Read(db.ChangeTracker);
        var roots = db.ChangeTracker.Entries<IAggregateRoot>().Select(e => e.Entity).ToList();
        List<IDomainEvent> events = [.. roots.SelectMany(r => r.DomainEvents)];
        foreach (var root in roots)
        {
            root.ClearDomainEvents();
        }

        IReadOnlyList<SideEffect> sideEffects =
            [.. events.Select(e => new SideEffect(e.GetType().Name, JsonSerializer.SerializeToNode(e, e.GetType(), Json)!))];
        return (changes, sideEffects, events);
    }

    private static object CauseOf(AppError error) => new { code = error.Code, message = error.Message };

    private PreviewStale Stale(ActionDescriptor action, string reason, object? cause)
    {
        LogStale(logger, action.Name, reason);
        return new PreviewStale(action.Name, reason, cause);
    }

    private static void RequireKind(ActionDescriptor action, ActionKind kind)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Kind != kind)
        {
            throw new ArgumentException($"'{action.Name}' is a {action.Kind}, not a {kind}.", nameof(action));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Execute of {Action} rejected: preview is stale ({Reason})")]
    private static partial void LogStale(ILogger logger, string action, string reason);
}
