# Outbox Phase 2: Transactional Outbox — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every side effect of a committed action is delivered at least once, through delivery rows written in the execute's transaction and drained by workers that claim them with `FOR UPDATE SKIP LOCKED`.

**Architecture:** The pipeline fans each raised event out to its subscribers (in-process handlers and publishers) as `OutboxDelivery` rows, saved in the same `SaveChanges` as the aggregate and its `History` rows. `OutboxProcessor` claims claimable rows through `OutboxStore` (the only class besides `ActionPipeline` allowed to change rows), delivers each in a fresh DI scope, and records success, retry with backoff, or dead. `OutboxWorker` (a `BackgroundService`) runs the processor; tests drive it with `DrainOutboxAsync()`. The in-process `DomainEventDispatcher` is removed.

**Tech Stack:** .NET 10, EF Core 10.0.15, Npgsql EF 10.0.4, SQLite (fast suite), Testcontainers.PostgreSql 4.17.0 on Podman, xUnit v3 (MTP), Shouldly, `FakeTimeProvider`.

**Spec:** `docs/superpowers/specs/2026-11-24-outbox-design.md`, §2 (handlers and publishers only), §3, §4, §6, §7, §8 and "Delivery order" item 2. Webhooks (§5) are phase 3. This plan builds the seams phase 3 plugs into: a subscriber key and a write-time subscriber lookup.

## Global Constraints

- Subscriber keys: `handler:<stable name>` and `publisher:<stable name>`. Stable names come from `[Subscriber("name")]` and match `^[a-z0-9-]+$`, unique across all subscribers.
- `OutboxDelivery` columns, exactly as spec §3: `Id` (long, store-generated), `HistoryEntryId` (FK, set through a navigation), `Subscriber`, `AggregateType`, `AggregateId`, `Status`, `Attempts`, `NextAttemptAt`, `LeaseUntil`, `LastError` (truncated to 2000 characters), `CompletedAt`. Table `OutboxDeliveries`. Indexes `(Subscriber, AggregateType, AggregateId, Id)` and `(Status, NextAttemptAt)`.
- `OutboxStatus` enum: `Pending`, `InFlight`, `Done`, `Dead`. Stored as the enum name (`HasConversion<string>()`, max length 20). The API's JSON emits `pending`, `in_flight`, `done`, `dead`.
- `OutboxOptions` (configuration section `Outbox`):
  - `WorkerEnabled = true`, `BatchSize = 20`, `PollInterval = 1 s`
  - `LeaseDuration = 60 s`, `HandlerTimeout = 30 s` (the timeout must stay below the lease)
  - `MaxAttempts = 10`, `BackoffBase = 10 s` (doubling), `BackoffCap = 1 h`
  - `RetentionDays = 7`
- Claimable means:
  - (`Pending` and `NextAttemptAt <= now`) or (`InFlight` and `LeaseUntil < now`),
  - and no row has the same `Subscriber`, `AggregateType` and `AggregateId`, a lower `Id`, and status `Pending` or `InFlight`.
- Delivery outcomes:
  - **Dead unblocks:** a `Dead` row lets the same aggregate's later rows through.
  - **Kept rows:** dead rows are kept; `Done` rows older than `RetentionDays` are deleted.
  - **Subscriber gone, or event version gone:** dead at once, never retried and never decoded best-effort.
- At-least-once delivery: handlers must be idempotent. Scoped `OutboxDeliveryContext` exposes `DeliveryId` and `Attempt`.
- Raw SQL lives only in `src/AppTemplate.Api/Data/OutboxStore.cs` (golden rule 6). Identifiers are double-quoted in both dialects.
- Every state change still goes through an action. `OutboxStore` is the one named exception in `StateChangeTests`: outbox rows are delivery bookkeeping, not business state.
- Spans and metrics use the existing `AppTemplate.Actions` source and meter (`ActionTelemetry.Name`), so `ServiceDefaults` needs no change.
- Look up every EF Core, Npgsql and hosting API before use (AGENTS.md Docs-lookup rule), and confirm it against `~/.nuget/packages`.
- Each task ends with `scripts/verify.sh` printing `VERIFY OK` and one Conventional Commit with the session's trailer.

## Review Focus

1. **A handler that hangs past `HandlerTimeout`:** the attempt is cancelled and counted as a failure (retry), so the lease never expires mid-delivery and runs the handler twice. Pinned in Task 3, `Drain_HandlerExceedsTimeout_RecordsFailure`.
2. **Shutdown mid-delivery:** host cancellation must not record a failure or burn an attempt; the row stays `InFlight` and is reclaimed after its lease. Pinned in Task 3, `Deliver_HostCancelled_LeavesRowInFlight`.
3. **One bad row must not stop the worker loop:** an exception building one delivery (corrupt payload, missing history row) marks that row and the batch continues. Pinned in Task 3, `Drain_PayloadUndeserializable_MarksDeadAndDeliversOthers`.
4. **Several pending rows for one aggregate:** the batch claims only the first, and drain keeps making progress until all are delivered in order. Pinned in Task 2, `Drain_ThreeEditsOneNote_DeliversInOrder`.
5. **An action with zero subscribers:** no rows and no empty insert, and the execute behaves exactly as before. Pinned in Task 1, `Execute_EventWithoutSubscribers_WritesNoDeliveries`.

---

### Task 1: Subscribers, `OutboxDelivery` and the write path

**Files:**
- Create: `src/AppTemplate.Api/Actions/Outbox/SubscriberAttribute.cs`, `OutboxSubscriberRegistry.cs`, `OutboxServiceCollectionExtensions.cs`, `OutboxOptions.cs`
- Create: `src/AppTemplate.Api/Data/OutboxDelivery.cs`, `src/AppTemplate.Api/Data/OutboxDeliveryConfiguration.cs`
- Modify: `src/AppTemplate.Api/Data/AppDbContext.cs` (add `DbSet<OutboxDelivery> OutboxDeliveries`)
- Modify: `src/AppTemplate.Api/Actions/ActionPipeline.cs:218-223` (history rows become a list; add deliveries)
- Modify: `src/AppTemplate.Api/Actions/ActionServiceCollectionExtensions.cs` (call `AddOutbox()`)
- Modify: `tests/AppTemplate.Api.Tests/Fixtures/Notes/NoteFixture.cs:27-29` (register through `AddEventHandler`), `RecordingEventHandler.cs` (add `[Subscriber("note-recorder")]`)
- Create (generated): migration `AddOutbox` for both providers via `scripts/add-migration.sh AddOutbox`
- Test: `tests/AppTemplate.Api.Tests/Outbox/OutboxWriteTests.cs`, `tests/AppTemplate.Api.Tests/Outbox/SubscriberRegistryTests.cs`

**Interfaces:**
- Produces:
  - `[AttributeUsage(Class)] public sealed class SubscriberAttribute(string name) : Attribute { public string Name { get; } public string[]? Events { get; init; } }`. `Events` filters publishers by `[Event]` name; null means all events.
  - `public static IServiceCollection AddEventHandler<THandler>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)`. It registers `THandler` and records one handler registration per `IDomainEventHandler<TEvent>` that `THandler` implements.
  - `public static IServiceCollection AddEventPublisher<TPublisher>(this IServiceCollection services) where TPublisher : class, IEventPublisher`. The `IEventPublisher` interface itself comes in Task 4; add this extension there.
  - `public sealed class OutboxSubscriberRegistry`, a singleton. It throws `InvalidOperationException` at construction if a name is missing, malformed or duplicated. Members:
    - `IReadOnlyList<string> KeysFor(Type eventType, string eventName)` returns the matching handler keys, then publisher keys, in stable order.
    - `bool TryGetHandler(string key, out HandlerRegistration registration)`
    - `Type? EventTypeFor(string eventName, int eventVersion)`
  - `public sealed record HandlerRegistration(string Key, Type HandlerType, IReadOnlyList<Type> EventTypes)`
  - `public sealed class OutboxDelivery`. Its constructor `OutboxDelivery(HistoryEntry entry, string subscriber, DateTimeOffset now)` sets `Status = Pending`, `NextAttemptAt = now` and `Attempts = 0`, and copies the aggregate type and id from the entry. It has the `HistoryEntry` navigation and the spec columns. EF uses a private parameterless constructor.
  - `public enum OutboxStatus { Pending, InFlight, Done, Dead }`
  - `public sealed class OutboxOptions` with the Global Constraints defaults, bound to section `Outbox`.

- [ ] **Step 1: Write the failing tests** (`OutboxWriteTests : ApiTestBase`, Notes fixture on):

```csharp
[Fact] public async Task Execute_NoteCreated_WritesOneDeliveryPerSubscriber()
{   var (_, e) = await PreviewAndExecute("fixture_create_note", new { text = "hello" });
    e.ShouldSucceed();
    var rows = await Deliveries();                       // helper: db.OutboxDeliveries.Include(d => d.HistoryEntry) ordered by Id
    rows.Count.ShouldBe(1);
    rows[0].Subscriber.ShouldBe("handler:note-recorder");
    rows[0].Status.ShouldBe(OutboxStatus.Pending);
    rows[0].HistoryEntry.EventType.ShouldBe("note.created");
    rows[0].AggregateType.ShouldBe(rows[0].HistoryEntry.AggregateType); }
[Fact] public async Task Preview_NoteCreated_WritesNoDeliveries()           // preview only; Deliveries() is empty
[Fact] public async Task Execute_StaleToken_WritesNoDeliveries()             // second execute of one edit token → stale; exactly 1 row (from the first)
[Fact] public async Task Execute_EventWithoutSubscribers_WritesNoDeliveries()   // factory without the recorder registration: event raised, 0 rows, execute succeeds
```

Check the actual Note event names in `tests/AppTemplate.Api.Tests/Fixtures/Notes/NoteEvents.cs` first.

`SubscriberRegistryTests` (unit tests on a `ServiceCollection`, no host):

```csharp
[Fact] public void Build_HandlerWithoutSubscriberAttribute_Throws()   // message names the handler type
[Fact] public void Build_DuplicateNames_Throws()                      // message names both types
[Fact] public void Build_MalformedName_Throws()                       // "Bad_Name"
[Fact] public void KeysFor_HandlerOfTwoEvents_ReturnsSameKeyForBoth()  // RecordingEventHandler: NoteCreated and NoteEdited → "handler:note-recorder"
```

- [ ] **Step 2: Run the tests and confirm RED.** Run `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~Outbox"`. Expected: a compile failure on `OutboxDelivery` and `AddEventHandler`.

- [ ] **Step 3: Implement the attribute, registry, options, entity, configuration and `AddOutbox()`.** Then switch `NoteFixture` to `services.AddEventHandler<RecordingEventHandler>(ServiceLifetime.Singleton)`. The existing PipelineTests resolve `RecordingEventHandler` directly, so it must remain resolvable as itself. `DomainEventDispatcher` still resolves `IDomainEventHandler<T>`, so `AddEventHandler` also registers those interfaces, forwarding to `THandler`. Task 2 deletes the dispatcher.

- [ ] **Step 4: Write deliveries in the pipeline.**
  - In `ExecuteCoreAsync`, materialise `HistoryRows(...)` to a list.
  - For each entry, add `new OutboxDelivery(entry, key, now)` for every key in `registry.KeysFor(raised.Event.GetType(), entry.EventType)`.
  - Do this before the single `SaveChangesAsync`.
  - Phase 3 adds webhook keys at this same call site.

- [ ] **Step 5: Generate the migration.** Run `scripts/add-migration.sh AddOutbox`. Read both migrations: they create the table, the FK to `History` and both indexes. Expected: `ProviderModelTests` passes.

- [ ] **Step 6: Run the tests and confirm GREEN.** Run `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~Outbox"`. Expected: 8 passed. The whole fast suite also stays green: the dispatcher still delivers, so PipelineTests are unchanged.

- [ ] **Step 7: Run `scripts/verify.sh` (expected: `VERIFY OK`), then commit.** Commit message: `feat(outbox): write a delivery row per event and subscriber in the execute transaction`.

---

### Task 2: `OutboxStore`, `OutboxProcessor`, worker and drain; remove in-process dispatch

**Files:**
- Create: `src/AppTemplate.Api/Data/OutboxStore.cs`
- Create: `src/AppTemplate.Api/Actions/Outbox/OutboxProcessor.cs`, `OutboxWorker.cs`, `OutboxDeliveryContext.cs`
- Delete: `src/AppTemplate.Api/Actions/Events/DomainEventDispatcher.cs`
- Modify: `src/AppTemplate.Api/Actions/ActionPipeline.cs` (remove the dispatch call at :243; update the `ExecuteAsync` remarks at :117-122)
- Modify: `src/AppTemplate.Api/Actions/Events/IDomainEventHandler.cs` (the doc now says delivery is through the outbox, at least once)
- Modify: `src/AppTemplate.Api/Actions/ActionServiceCollectionExtensions.cs:66`
- Modify: `tests/AppTemplate.Api.Tests/Infrastructure/ApiTestBase.cs` (add `DrainOutboxAsync`), `ApiFactory.cs` (`Settings["Outbox:WorkerEnabled"] = "false"`)
- Modify: `tests/AppTemplate.Api.Tests/Actions/PipelineTests.cs:30-78` (call `await DrainOutboxAsync()` before reading `Received`)
- Modify: `tests/AppTemplate.Architecture.Tests/StateChangeTests.cs` (allow owner `OutboxStore`; also flag `SqlQuery*` as state-changing)
- Test: `tests/AppTemplate.Api.Tests/Outbox/OutboxDeliveryTests.cs`, `tests/AppTemplate.Architecture.Tests/StateChangeTests.cs`

**Interfaces:**
- Consumes: `OutboxDelivery`, `OutboxStatus`, `OutboxOptions`, `OutboxSubscriberRegistry` (Task 1).
- Produces:
  - `public sealed class OutboxStore(AppDbContext db)`, scoped, with these members:
    - `Task<IReadOnlyList<long>> ClaimAsync(int batchSize, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken ct)`: one `UPDATE … RETURNING "Id"` through `db.Database.SqlQuery<long>`. On Npgsql (`db.Database.IsNpgsql()`) the subquery adds `FOR UPDATE SKIP LOCKED`. It sets `Status = 'InFlight'`, `LeaseUntil` and `Attempts = Attempts + 1`, and orders by `Id`.
    - `Task CompleteAsync(long id, int attempt, DateTimeOffset now, CancellationToken ct)`
    - `Task FailAsync(long id, int attempt, string error, DateTimeOffset nextAttemptAt, CancellationToken ct)`
    - `Task MarkDeadAsync(long id, int attempt, string error, CancellationToken ct)`
    - `Task<int> DeleteDoneBeforeAsync(DateTimeOffset cutoff, CancellationToken ct)`
  - The three status updates are `ExecuteUpdateAsync` guarded by `Id == id && Status == InFlight && Attempts == attempt`, so a worker whose row was reclaimed cannot overwrite the newer attempt.
  - `public sealed class OutboxProcessor(IServiceScopeFactory scopes, OutboxSubscriberRegistry registry, TimeProvider time, IOptions<OutboxOptions> options, IOptions<JsonOptions> json, ILogger<OutboxProcessor> log)`, a singleton, with:
    - `Task<int> RunOnceAsync(CancellationToken ct)`, which returns the number of rows claimed.
    - `Task<int> DrainAsync(CancellationToken ct)`, which loops `RunOnceAsync` until it returns 0. It throws `InvalidOperationException` after 1000 rounds, so a test bug can't hang the suite.
  - Each row is delivered in a fresh scope. That scope's `OutboxDeliveryContext` is set first, then the handler is resolved from it, the event is deserialized with the API's JSON options into `registry.EventTypeFor(...)`, and `IDomainEventHandler<TEvent>.Handle` is called through a cached generic delegate. The delegate cache follows the pattern of the deleted dispatcher.
  - `public sealed class OutboxDeliveryContext { public long DeliveryId { get; internal set; } public int Attempt { get; internal set; } }`, registered scoped.
  - `internal sealed class OutboxWorker : BackgroundService`. It does nothing when `WorkerEnabled` is false. Otherwise it loops: `RunOnceAsync`, then waits `PollInterval` when 0 rows came back, and deletes done rows older than `RetentionDays` at most once an hour. It logs and continues on exceptions other than host cancellation.
  - `protected Task<int> DrainOutboxAsync()` on `ApiTestBase`, which calls `OutboxProcessor.DrainAsync` with `TestContext.Current.CancellationToken`.

- [ ] **Step 1: Write the failing tests** (`OutboxDeliveryTests : ApiTestBase`):

```csharp
[Fact] public async Task Drain_NoteCreated_DeliversToHandlerAndMarksDone()
{   await CreateNote("hello");
    (await DrainOutboxAsync()).ShouldBe(1);
    Recorder.Received.OfType<NoteCreated>().Single().Text.ShouldBe("hello");
    (await Deliveries()).Single().Status.ShouldBe(OutboxStatus.Done); }
[Fact] public async Task Execute_BeforeDrain_HandlerNotCalled()             // Recorder.Received empty until DrainOutboxAsync
[Fact] public async Task Drain_ThreeEditsOneNote_DeliversInOrder()         // create + 3 edits; Received texts in edit order; all 4 rows Done
[Fact] public async Task Drain_Twice_DeliversOnce()                         // second DrainOutboxAsync returns 0; Received.Count == 1
[Fact] public async Task Deliver_Handler_SeesDeliveryContext()             // a context-capturing fixture handler records DeliveryId == row.Id, Attempt == 1
```

`StateChangeTests`: add `StateChanges_OnlyOutboxStoreBesidesPipeline`. It asserts that every hit outside `ActionPipeline` is owned by `OutboxStore`, and that `OutboxStore`'s hits are non-empty, which proves the guard actually sees its calls.

- [ ] **Step 2: Run the tests and confirm RED.** Run `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~OutboxDeliveryTests"`. Expected: a compile failure on `DrainOutboxAsync`.

- [ ] **Step 3: Implement `OutboxStore`, `OutboxProcessor`, `OutboxDeliveryContext` and `OutboxWorker`.** Register them in `AddOutbox()`. The worker is registered with `AddHostedService`.

- [ ] **Step 4: Delete the dispatcher and its call.**
  - Remove `DomainEventDispatcher.cs`, its registration and the call at `ActionPipeline.cs:243`.
  - Rewrite the `ExecuteAsync` remarks: events reach subscribers through outbox rows committed with the change.
  - Make `AddEventHandler` stop registering the `IDomainEventHandler<T>` forwards.
  - Update PipelineTests :30-78 to drain before asserting.

- [ ] **Step 5: Run the tests and confirm GREEN.** Run `dotnet test --project tests/AppTemplate.Api.Tests` and `dotnet test --project tests/AppTemplate.Architecture.Tests`. Expected: all pass. Then temporarily drop the `NOT EXISTS` ordering clause and confirm `Drain_ThreeEditsOneNote_DeliversInOrder` fails; revert.

- [ ] **Step 6: Run `scripts/verify.sh` (expected: `VERIFY OK`), then commit.** Commit message: `feat(outbox): deliver events from the outbox and drop in-process dispatch`.

---

### Task 3: Failure handling: retry, dead, isolation, timeouts, retention

**Files:**
- Modify: `src/AppTemplate.Api/Actions/Outbox/OutboxProcessor.cs`
- Create: `tests/AppTemplate.Api.Tests/Fixtures/Notes/FlakyNoteHandler.cs` (`[Subscriber("note-flaky")]`, a singleton whose failure mode the test sets: throw N times, hang, or succeed)
- Test: `tests/AppTemplate.Api.Tests/Outbox/OutboxFailureTests.cs`

**Interfaces:**
- Consumes: Task 2's `OutboxStore` and `OutboxProcessor`.
- Produces: the backoff as `internal static TimeSpan Backoff(int attempts, OutboxOptions o)`, which returns `min(BackoffBase × 2^(attempts−1), BackoffCap)`. Attempt 1 is 10 s and attempt 10 is capped at 1 h.

- [ ] **Step 1: Write the failing tests.** `FlakyNoteHandler` is registered only in this test class (`ConfigureServices`), next to the recorder.

```csharp
[Fact] public async Task Drain_HandlerThrows_RetriesAfterBackoff()
{   Flaky.FailTimes = 1; await CreateNote("a");
    await DrainOutboxAsync();
    var row = await Delivery("handler:note-flaky");
    row.Status.ShouldBe(OutboxStatus.Pending); row.Attempts.ShouldBe(1);
    row.NextAttemptAt.ShouldBe(Factory.Time.GetUtcNow().AddSeconds(10)); row.LastError.ShouldNotBeNullOrEmpty();
    Factory.Time.Advance(TimeSpan.FromSeconds(10)); await DrainOutboxAsync();
    (await Delivery("handler:note-flaky")).Status.ShouldBe(OutboxStatus.Done); }
[Fact] public async Task Drain_HandlerAlwaysFails_DeadAfterMaxAttempts()   // advance through backoff each round; Status Dead, Attempts == 10
[Fact] public async Task Drain_DeadRow_UnblocksLaterRowsOfAggregate()     // fail create forever → dead; then the edit's row is delivered
[Fact] public async Task Drain_EarlierRowRetrying_BlocksLaterRowOfSameAggregate()  // edit row stays Pending (Attempts 0) while create retries
[Fact] public async Task Drain_FailingSubscriber_DoesNotBlockOtherSubscriber()     // recorder's row Done while flaky retries
[Fact] public async Task Drain_HandlerExceedsTimeout_RecordsFailure()     // Settings Outbox:HandlerTimeout=00:00:01, handler hangs; row Pending, Attempts 1, LastError mentions timeout
[Fact] public async Task Deliver_HostCancelled_LeavesRowInFlight()        // cancel the token passed to RunOnceAsync during Handle; row InFlight, LastError null
[Fact] public async Task Drain_LeaseExpired_ReclaimsInFlightRow()        // set the row InFlight with LeaseUntil in the past through OutboxStore; drain delivers it, Attempts 2
[Fact] public async Task Drain_HandlerNoLongerRegistered_MarksDead()     // write the row with the flaky handler registered, then rebuild the host without it on the same database
[Fact] public async Task Drain_EventVersionUnknown_MarksDead()           // tamper HistoryEntry.EventVersion to 99 through the test context; Dead, LastError names note.created v99
[Fact] public async Task Drain_PayloadUndeserializable_MarksDeadAndDeliversOthers()  // corrupt one payload; that row Dead, a second note's row Done
[Fact] public async Task Retention_DoneRowsOlderThanRetention_AreDeleted()  // via OutboxStore.DeleteDoneBeforeAsync(now - 7 days); Dead rows kept
```

The two "rebuild the host" and "tamper" tests change rows directly from test code, which `StateChangeTests` allows because it scans only the Api assembly. If rebuilding a host on the same SQLite connection is impractical with `ApiFactory`, rule on it: register the handler conditionally on a mutable test flag that the registry reads when it resolves the handler, and ledger the ruling.

- [ ] **Step 2: Run the tests and confirm RED.** Run `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~OutboxFailureTests"`. Expected: the retry, dead and timeout tests fail.

- [ ] **Step 3: Implement the outcomes** in `OutboxProcessor`:
  - **Failure:** wrap `Handle` in a linked token with `CancelAfter(HandlerTimeout)`. A timeout or exception goes to `FailAsync` with `Backoff(attempt)`, or to `MarkDeadAsync` when `attempt >= MaxAttempts`.
  - **Host cancellation:** writes nothing.
  - **Build failures:** an unknown subscriber, unknown event version or deserialization failure goes to `MarkDeadAsync` with a reason.
  - **Logging:** log a dead row at Warning with the delivery id, subscriber and event name, using `[LoggerMessage]`.
  - **Truncation:** cut `LastError` to 2000 characters.

- [ ] **Step 4: Run the tests and confirm GREEN.** Run `dotnet test --project tests/AppTemplate.Api.Tests`. Expected: all pass.

- [ ] **Step 5: Run `scripts/verify.sh` (expected: `VERIFY OK`), then commit.** Commit message: `feat(outbox): retry with backoff, dead-letter and isolate failing subscribers`.

---

### Task 4: Publishers and the envelope

**Files:**
- Create: `src/AppTemplate.Api/Actions/Outbox/IEventPublisher.cs`, `OutboxEnvelope.cs`
- Modify: `OutboxServiceCollectionExtensions.cs` (`AddEventPublisher<T>`), `OutboxSubscriberRegistry.cs` (publisher keys, the `Events` filter), `OutboxProcessor.cs` (publisher delivery)
- Create: `tests/AppTemplate.Api.Tests/Fixtures/Notes/RecordingPublisher.cs` (`[Subscriber("note-bus")]`, a singleton)
- Test: `tests/AppTemplate.Api.Tests/Outbox/OutboxPublisherTests.cs`

**Interfaces:**
- Produces:
  - `public interface IEventPublisher { Task Publish(OutboxEnvelope envelope, CancellationToken ct); }`
  - `public sealed record OutboxEnvelope(long Id, string Event, int EventVersion, string AggregateType, string AggregateId, int AggregateVersion, DateTimeOffset OccurredAt, string? TraceId, JsonElement Data)`. These are exactly the webhook envelope fields (spec §5), which phase 3 serializes. `Data` is the stored payload, parsed and never re-serialized from a CLR type.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public async Task Drain_Publisher_ReceivesEnvelopeOfEveryEvent()
{   var id = await CreateNote("hello"); await EditNote(id, "world"); await DrainOutboxAsync();
    var env = Publisher.Received; env.Count.ShouldBe(2);
    env[0].Event.ShouldBe("note.created"); env[0].EventVersion.ShouldBe(1);
    env[0].AggregateId.ShouldBe(id.ToString()); env[0].Data.GetProperty("text").GetString().ShouldBe("hello");
    env[0].Id.ShouldBe((await Delivery("publisher:note-bus", 0)).Id); }
[Fact] public async Task Drain_PublisherWithEventsFilter_ReceivesOnlyListed()  // [Subscriber("note-edits", Events = ["note.edited"])] gets 1 of 2
[Fact] public async Task Drain_PublisherEventVersionBumped_StillDelivers()     // tamper EventVersion to 99: the publisher passes the payload through, row Done
```

- [ ] **Step 2: Run the tests and confirm RED.** Expected: a compile failure on `IEventPublisher`.

- [ ] **Step 3: Implement it.** Publisher failures use Task 3's retry and dead path unchanged.

- [ ] **Step 4: Run the tests and confirm GREEN.** Run `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~Outbox"`. Expected: all pass.

- [ ] **Step 5: Run `scripts/verify.sh` (expected: `VERIFY OK`), then commit.** Commit message: `feat(outbox): deliver envelopes to event publishers`.

---

### Task 5: Telemetry and the `list_outbox_deliveries` query

**Files:**
- Create: `src/AppTemplate.Api/Actions/Telemetry/OutboxTelemetry.cs` (span and tag names, counters, a lag histogram on the `ActionTelemetry` source and meter)
- Modify: `OutboxProcessor.cs` (a span per attempt)
- Create: `src/AppTemplate.Api/Features/Outbox/ListOutboxDeliveries.cs`, `OutboxValidators.cs`
- Modify: `src/web/openapi.json`, `src/web/src/api/schema.d.ts` (generated)
- Test: `tests/AppTemplate.Api.Tests/Outbox/OutboxTelemetryTests.cs`, `tests/AppTemplate.Api.Tests/Features/Outbox/ListOutboxDeliveriesTests.cs`

**Interfaces:**
- Produces:
  - A span named `outbox deliver {subscriber}` with tags `outbox.subscriber`, `outbox.attempt`, `outbox.outcome` (`done`, `retry`, `dead`), `event.name` and `aggregate.type`. It carries an `ActivityLink` to the history row's `TraceId` when one is set.
  - Counter `outbox.deliveries` tagged `outbox.outcome`, and histogram `outbox.delivery.lag` in ms (now minus `OccurredAt`, recorded on `done`).
  - `[Query("list_outbox_deliveries", "…")] public sealed class ListOutboxDeliveries : IQuery<ListOutboxDeliveriesInput, IReadOnlyList<OutboxDeliveryDto>>`
  - `public sealed record ListOutboxDeliveriesInput(OutboxStatus? Status = null, string? Subscriber = null, string? AggregateType = null, string? AggregateId = null, int? Limit = null, long? BeforeId = null)`. Results are newest first; `Limit` runs 1 to 200 and defaults to 50.
  - `public sealed record OutboxDeliveryDto(long Id, string Subscriber, string Event, string AggregateType, string AggregateId, OutboxStatus Status, int Attempts, DateTimeOffset NextAttemptAt, DateTimeOffset? LeaseUntil, DateTimeOffset? CompletedAt, string? LastError, long HistoryEntryId)`. It carries no payload.
  - The query description suggests the next step: "Find deliveries that are dead or stuck; get_history shows the event itself."

- [ ] **Step 1: Write the failing tests.**
  - `ListOutboxDeliveriesTests`, mirroring `GetHistoryTests`:
    - `[Covers("list_outbox_deliveries", Case.HappyPath)] Query_StatusDead_ReturnsOnlyDeadNewestFirst`
    - `[Covers("list_outbox_deliveries", "validation.failed")] Query_LimitZero_ReturnsFieldError` (field code `outbox.limit_out_of_range`)
    - `Query_BeforeId_PagesBack`
  - `OutboxTelemetryTests` (fixture handlers only):
    - `Drain_Success_EmitsDeliverSpanWithDoneOutcome`, which checks the span name `outbox deliver handler:note-recorder`, `outbox.outcome = done`, and a link whose trace id equals the history row's `TraceId`.
    - `Drain_Failure_EmitsRetryOutcome`. Use `Factory.Activities`.

- [ ] **Step 2: Run the tests and confirm RED.** Run `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~ListOutboxDeliveries|FullyQualifiedName~OutboxTelemetry"`. Expected: a compile failure.

- [ ] **Step 3: Implement it.** Follow `skill:add-action` for the query (`GetHistory` is the template). Then run `scripts/update-openapi.sh` and `cd src/web && npm run gen:api`.

- [ ] **Step 4: Run the tests and confirm GREEN.** Run `dotnet test --project tests/AppTemplate.Api.Tests` and `dotnet test --project tests/AppTemplate.Architecture.Tests`. Expected: all pass, including `CoverageTests`.

- [ ] **Step 5: Run `scripts/verify.sh` (expected: `VERIFY OK`, no client drift), then commit.** Commit message: `feat(outbox): trace deliveries and list them with list_outbox_deliveries`.

---

### Task 6: Postgres proof of the claim

**Files:**
- Modify: `tests/AppTemplate.Api.PostgresTests/PostgresApiFactory.cs` (an optional `Action<IServiceCollection>` and `Outbox:WorkerEnabled=false`), `PostgresTestBase.cs` (`Services`, `DrainOutboxAsync`)
- Create: `tests/AppTemplate.Api.PostgresTests/ProjectCreatedRecorder.cs` (`[Subscriber("project-recorder")]`, a singleton)
- Test: `tests/AppTemplate.Api.PostgresTests/OutboxClaimTests.cs`

**Interfaces:**
- Consumes: `OutboxStore.ClaimAsync`, `OutboxProcessor.DrainAsync` (Task 2), `AddEventHandler` (Task 1).

- [ ] **Step 1: Write the tests.**

```csharp
[Fact] public async Task Execute_ThenDrain_DeliversOnPostgres()           // create_project → recorder got ProjectCreated; row Done
[Fact] public async Task ClaimAsync_TwoConcurrentClaimers_NeverClaimSameRow()
{   // 40 projects → 40 pending rows (one aggregate each); 20 rounds of Task.WhenAll(two ClaimAsync(batch 5) on separate contexts)
    claimed.Count.ShouldBe(40); claimed.Distinct().Count().ShouldBe(40); }
[Fact] public async Task ClaimAsync_SameAggregateRows_ClaimsOnlyEarliest()   // create + 2 renames of one project → claim returns only the create's row
[Fact] public async Task ClaimAsync_LeaseExpired_ReclaimsRow()               // claim with a lease in the past, claim again → same id, Attempts 2
```

- [ ] **Step 2: Run them and prove they can fail.** Run `dotnet test --project tests/AppTemplate.Api.PostgresTests --filter "FullyQualifiedName~OutboxClaimTests"`. Expected: pass. Then temporarily drop `FOR UPDATE SKIP LOCKED` and the `IsNpgsql()` branch, and confirm `TwoConcurrentClaimers` fails with duplicate claims or blocks; revert. If it does not fail, raise the rounds to 100 and ledger the result.

- [ ] **Step 3: Run `scripts/verify.sh` (expected: `VERIFY OK` with the Postgres tests), then commit.** Commit message: `test(outbox): prove SKIP LOCKED claiming and ordering on Postgres`.

---

### Task 7: Documentation

**Files:**
- Modify: `AGENTS.md`:
  - a new "Outbox and side effects" section after "History": subscribers, `[Subscriber]`, at-least-once delivery, idempotent handlers, `OutboxDeliveryContext`, per-aggregate ordering, dead rows, `list_outbox_deliveries` and `DrainOutboxAsync` in tests
  - the History bullet on bumping an event version gains "drain the outbox before deploying"
  - the dotnet-claude-kit overrides row on domain events now says delivered through the outbox
- Modify: `.claude/skills/tdd/SKILL.md` (assert on side effects after `DrainOutboxAsync()`; fixture handlers are idempotent), `.claude/skills/investigate/SKILL.md` (a missing side effect leads to `list_outbox_deliveries` for the aggregate, then `get_history`)
- Modify: `docs/superpowers/specs/2026-11-24-outbox-design.md` (Status: phase 2 done)

- [ ] **Step 1: Find stale references.** Run `grep -rn -i "DomainEventDispatcher\|best-effort\|no outbox\|dispatched after" AGENTS.md .claude src --include=*.md --include=*.cs || true`. Every hit is fixed or kept for a stated reason.
- [ ] **Step 2: Write the sections.**
- [ ] **Step 3: Run `scripts/verify.sh` (expected: `VERIFY OK`; `SkillLintTests` checks the paths), then commit.** Commit message: `docs: describe the outbox, subscribers and delivery guarantees`.
