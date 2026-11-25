using System.Diagnostics;
using System.Text.Json;
using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Actions.Telemetry;
using AppTemplate.Api.Actions.Preview;
using AppTemplate.Api.Actions.Validation;
using AppTemplate.Api.Data;
using AppTemplate.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
    ILogger<ActionPipeline> logger,
    ActionMetrics metrics,
    OutboxSubscriberRegistry subscribers)
{
    /// <summary>Stale reasons reported in <c>details.reason</c> of <c>preview.stale</c>.</summary>
    public static class StaleReasons
    {
        public const string VersionChanged = "version_changed";
        public const string DomainError = "domain_error";
        public const string ChangesDiffer = "changes_differ";
        public const string ConcurrentWrite = "concurrent_write";
    }

    /// <summary>The optional execute envelope field stored on history rows. Reserved: no input record may declare it.</summary>
    public const string ReasonProperty = "reason";

    private JsonSerializerOptions Json => jsonOptions.Value.SerializerOptions;

    /// <summary>
    /// Load, Summarize and Apply without saving; returns the change set and a signed token that binds it.
    /// Domain events are reported as side effects, then cleared; a preview never dispatches them.
    /// </summary>
    public Task<OneOf<PreviewResponse, AppError>> PreviewAsync(ActionDescriptor action, JsonElement body, CancellationToken ct)
    {
        RequireKind(action, ActionKind.Mutation);
        return InstrumentAsync(action, "preview", activity => PreviewCoreAsync(action, body, activity, ct));
    }

    private async Task<OneOf<PreviewResponse, AppError>> PreviewCoreAsync(
        ActionDescriptor action, JsonElement body, Activity? activity, CancellationToken ct)
    {
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
        RequireTracked(action, db, target);
        TagAggregate(activity, db, target);
        var versions = AggregateVersions(db);
        var summary = action.Summarize!(handler, target, input);

        var applied = await action.ApplyAsync!(handler, target, input, ctx);
        if (applied.Index != 0)
        {
            return (AppError)applied.Value;
        }

        var (changes, sideEffects, _, _) = ReadChangeSet(action, db);
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
    /// version check, Apply, change-set hash, version bump (only when something changed), history and outbox rows,
    /// save (skipped when nothing changed and no event was raised), <c>ToResult</c>. History and outbox rows commit in
    /// the same SaveChanges as the aggregate; the outbox worker delivers the events to subscribers afterwards, at least
    /// once, so a subscriber failure never affects the execute's response.
    /// </remarks>
    /// <param name="caller">The channel and client recorded on the history rows.</param>
    public Task<OneOf<ExecuteResponse, AppError>> ExecuteAsync(ActionDescriptor action, JsonElement body, ActionCaller caller, CancellationToken ct)
    {
        RequireKind(action, ActionKind.Mutation);
        ArgumentNullException.ThrowIfNull(caller);
        return InstrumentAsync(action, "execute", activity => ExecuteCoreAsync(action, body, caller, activity, ct));
    }

    private async Task<OneOf<ExecuteResponse, AppError>> ExecuteCoreAsync(
        ActionDescriptor action, JsonElement body, ActionCaller caller, Activity? activity, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var parsed = await ReadInputAsync(action, body, services, ct);
        if (parsed.TryPickT1(out var invalid, out var input))
        {
            return invalid;
        }

        if (ReadReason(body).TryPickT1(out var badReason, out var reason))
        {
            return badReason;
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
        if (loaded.Index == 0)
        {
            RequireTracked(action, db, loaded.Value);
            TagAggregate(activity, db, loaded.Value);
        }

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

        var (changes, sideEffects, events, changedRoots) = ReadChangeSet(action, db);
        if (!string.Equals(ChangeSetHasher.Hash(changes, sideEffects, ctx.Warnings), token.ChangesHash, StringComparison.Ordinal))
        {
            return Stale(action, StaleReasons.ChangesDiffer, cause: null);
        }

        // Every aggregate the execute changed gets a new Version, so its concurrency token guards the save and other
        // open previews of it go stale. An unchanged aggregate keeps its Version, so its other open previews stay valid.
        foreach (var root in changedRoots)
        {
            root.IncrementVersion();
        }

        // History rows are added after the change set is read and hashed, so they never appear in a response,
        // and they commit in the same SaveChanges as the aggregate.
        if (changes.Count > 0 || events.Count > 0)
        {
            var historyRows = HistoryRows(action, events, caller, reason).ToList();
            db.History.AddRange(historyRows);

            // One outbox row per event and subscriber, committed with the change; the outbox worker delivers them.
            db.OutboxDeliveries.AddRange(DeliveryRows(events, historyRows));
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

        return new ExecuteResponse(action.Name, action.ToResult!(handler, target), changes);
    }

    /// <summary>Runs a query in its own scope and returns its output.</summary>
    public Task<OneOf<object, AppError>> QueryAsync(ActionDescriptor action, JsonElement body, CancellationToken ct)
    {
        RequireKind(action, ActionKind.Query);
        return InstrumentAsync(action, "query", _ => QueryCoreAsync(action, body, ct));
    }

    private async Task<OneOf<object, AppError>> QueryCoreAsync(ActionDescriptor action, JsonElement body, CancellationToken ct)
    {
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

        if (MissingRequiredField(action.InputType, body) is { } missing)
        {
            return new RequestMalformed($"The request body is missing the required field '{missing}' of '{action.Name}'.", $"$.{missing}");
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

    /// <summary>
    /// The JSON name of the first required input field absent from <paramref name="body"/>, or null. Required means
    /// <c>required</c>/<c>[JsonRequired]</c>, or a constructor parameter without a default value (the serializer
    /// enforces the same with <c>RespectRequiredConstructorParameters</c>, but reports only <c>$</c> as the path).
    /// </summary>
    private string? MissingRequiredField(Type inputType, JsonElement body)
    {
        var comparison = Json.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var property in Json.GetTypeInfo(inputType).Properties)
        {
            var required = property.IsRequired || property.AssociatedParameter is { HasDefaultValue: false, IsMemberInitializer: false };
            if (required && !body.EnumerateObject().Any(p => string.Equals(p.Name, property.Name, comparison)))
            {
                return property.Name;
            }
        }

        return null;
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

    /// <summary>
    /// Pending changes, the raised domain events as side effects, the events with their owning root (then cleared), and
    /// every aggregate root that owns at least one change: these are the roots whose <c>Version</c> the execute bumps.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An event has no <c>[Event]</c>, an aggregate changed without raising an event of its own, or a change has no
    /// tracked owning root. All are programming errors: history must be a complete record, so the call fails as
    /// <c>internal.unexpected</c> and nothing commits.
    /// </exception>
    private (IReadOnlyList<Change> Changes, IReadOnlyList<SideEffect> SideEffects, IReadOnlyList<RaisedEvent> Events, IReadOnlyList<IAggregateRoot> ChangedRoots)
        ReadChangeSet(ActionDescriptor action, AppDbContext db)
    {
        var changes = ChangeSetReader.Read(db.ChangeTracker);
        var changedRoots = ChangedRoots(action, db);
        var roots = db.ChangeTracker.Entries<IAggregateRoot>().ToList();
        List<RaisedEvent> events = [.. roots.SelectMany(root => root.Entity.DomainEvents.Select(e =>
            new RaisedEvent(e, root.Entity, root.Metadata.ClrType.Name, ChangeSetReader.KeyOf(root))))];
        foreach (var root in roots)
        {
            root.Entity.ClearDomainEvents();
        }

        foreach (var raised in events)
        {
            EventNames.Of(raised.Event.GetType());
        }

        foreach (var root in changedRoots.Where(r => !events.Any(e => ReferenceEquals(e.Root, r.Entity))))
        {
            throw new InvalidOperationException(
                $"Action '{action.Name}' changed {root.Metadata.ClrType.Name} {ChangeSetReader.KeyOf(root)} without raising a domain event "
                + "on that aggregate. Every method that changes state must raise an event so the change is recorded in history.");
        }

        IReadOnlyList<SideEffect> sideEffects =
            [.. events.Select(r => new SideEffect(r.Event.GetType().Name, JsonSerializer.SerializeToNode(r.Event, r.Event.GetType(), Json)!))];
        return (changes, sideEffects, events, [.. changedRoots.Select(r => r.Entity)]);
    }

    /// <summary>The distinct tracked roots owning the pending changes, in tracker order.</summary>
    private static List<EntityEntry<IAggregateRoot>> ChangedRoots(ActionDescriptor action, AppDbContext db)
    {
        List<EntityEntry<IAggregateRoot>> roots = [];
        foreach (var entry in ChangeSetReader.ChangedEntries(db.ChangeTracker))
        {
            var root = ChangeSetReader.RootOf(entry) ?? throw new InvalidOperationException(
                $"Action '{action.Name}' changed {entry.Metadata.ClrType.Name} {ChangeSetReader.KeyOf(entry)}, which no tracked aggregate root owns. "
                + "Load the owning aggregate and change the entity through it.");
            if (!roots.Any(r => ReferenceEquals(r.Entity, root.Entity)))
            {
                roots.Add(root.Context.Entry((IAggregateRoot)root.Entity));
            }
        }

        return roots;
    }

    /// <summary>One delivery row per (event, subscriber); <paramref name="historyRows"/> pairs with <paramref name="events"/> by index.</summary>
    private IEnumerable<OutboxDelivery> DeliveryRows(IReadOnlyList<RaisedEvent> events, IReadOnlyList<HistoryEntry> historyRows)
    {
        var now = time.GetUtcNow();
        for (var i = 0; i < events.Count; i++)
        {
            foreach (var key in subscribers.KeysFor(events[i].Event.GetType(), historyRows[i].EventType))
            {
                yield return new OutboxDelivery(historyRows[i], key, now);
            }
        }
    }

    /// <summary>One history row per raised event, all sharing the execute's time and execution id.</summary>
    private IEnumerable<HistoryEntry> HistoryRows(ActionDescriptor action, IReadOnlyList<RaisedEvent> events, ActionCaller caller, string? reason)
    {
        var occurredAt = time.GetUtcNow();
        var executionId = Guid.CreateVersion7();
        var traceId = Activity.Current?.TraceId.ToString();
        foreach (var raised in events)
        {
            var name = EventNames.Of(raised.Event.GetType());
            yield return new HistoryEntry(
                raised.AggregateType,
                raised.AggregateId,
                raised.Root.Version,
                name.Name,
                name.Version,
                JsonSerializer.Serialize(raised.Event, raised.Event.GetType(), Json),
                occurredAt,
                executionId,
                action.Name,
                caller.Channel,
                caller.Client,
                reason,
                traceId);
        }
    }

    /// <summary>
    /// The optional <c>reason</c> envelope field: trimmed, with empty, whitespace, JSON null and absent all meaning none.
    /// Not part of the input or the preview token, so any reason may accompany a confirmed preview.
    /// </summary>
    private static OneOf<string?, AppError> ReadReason(JsonElement body)
    {
        if (!body.TryGetProperty(ReasonProperty, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return (string?)null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return new RequestMalformed("The optional 'reason' must be a string.", "$.reason");
        }

        var reason = element.GetString()!.Trim();
        if (reason.Length > HistoryEntry.ReasonMaxLength)
        {
            return new RequestMalformed(
                $"The 'reason' is {reason.Length} characters; shorten it to at most {HistoryEntry.ReasonMaxLength}.", "$.reason");
        }

        return reason.Length == 0 ? null : reason;
    }

    /// <summary>A domain event together with the tracked root that raised it.</summary>
    private sealed record RaisedEvent(IDomainEvent Event, IAggregateRoot Root, string AggregateType, string AggregateId);

    /// <summary>
    /// Wraps one phase in its span, records metrics once, and logs an <see cref="AppError"/> result once at the boundary.
    /// Unhandled exceptions are tagged <c>internal.unexpected</c> and rethrown; the host's exception handlers log them.
    /// </summary>
    private async Task<OneOf<T, AppError>> InstrumentAsync<T>(
        ActionDescriptor action, string phase, Func<Activity?, Task<OneOf<T, AppError>>> run)
    {
        using var activity = ActionTelemetry.Source.StartActivity($"action {action.Name} {phase}");
        activity?.SetTag(ActionTelemetry.TagName, action.Name);
        activity?.SetTag(ActionTelemetry.TagPhase, phase);
        var started = Stopwatch.GetTimestamp();
        var outcome = ActionTelemetry.OutcomeUnexpected;
        try
        {
            var result = await run(activity);
            if (result.TryPickT1(out var error, out _))
            {
                outcome = error.Code;
                if (error is PreviewStale stale)
                {
                    activity?.SetTag(ActionTelemetry.TagStaleReason, stale.Reason);
                }

                ActionLog.ActionFailed(logger, action.Name, phase, error.Code, (activity ?? Activity.Current)?.TraceId.ToString() ?? "");
            }
            else
            {
                outcome = ActionTelemetry.OutcomeSuccess;
            }

            return result;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            activity?.SetTag(ActionTelemetry.TagOutcome, outcome);
            metrics.Record(action.Name, phase, outcome, Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>
    /// The change set is read from the change tracker, so an untracked aggregate (loaded with <c>AsNoTracking</c>)
    /// would preview no changes and execute as a silent no-op. Fail loudly instead: the exception surfaces as
    /// <c>internal.unexpected</c> and is logged with the handler type.
    /// </summary>
    private static void RequireTracked(ActionDescriptor action, AppDbContext db, object target)
    {
        if (db.Entry(target).State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                $"{action.HandlerType.FullName}.Load returned an untracked {target.GetType().Name} for '{action.Name}'. "
                + "Load must return a tracked aggregate (no AsNoTracking): query it from ctx.Db with tracking, or ctx.Db.Add a new one.");
        }
    }

    private static void TagAggregate(Activity? activity, AppDbContext db, object target)
    {
        if (activity is null)
        {
            return;
        }

        var entry = db.Entry(target);
        activity.SetTag(ActionTelemetry.TagAggregateType, entry.Metadata.ClrType.Name);
        activity.SetTag(ActionTelemetry.TagAggregateId, ChangeSetReader.KeyOf(entry));
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
