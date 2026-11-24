# Transactional outbox: design

Date: 2026-11-24. Status: draft for approval.

## Intent

Every side effect of a committed action is delivered reliably, even if the process crashes right after the commit. Today `DomainEventDispatcher` calls `IDomainEventHandler<T>` in-process after `SaveChanges` and delivery is best-effort: a crash or a handler exception loses the side effect. This design replaces that dispatch with a transactional outbox, drained by workers that claim rows from a simple Postgres queue using `FOR UPDATE SKIP LOCKED`.

Three kinds of subscriber, all on the same queue (user, 2026-11-24):

1. **In-process handlers**: the existing `IDomainEventHandler<T>`.
2. **External systems**: a broker or bus, reached through a publisher seam.
3. **Webhooks**: HTTP endpoints registered at runtime through actions, so runtime agents can manage them over MCP.

## Goals

- Write one delivery row per (event, subscriber) **in the same transaction** as the aggregate change and its `History` rows.
- Deliver at least once, with independent retries per subscriber: a dead webhook never delays a local handler.
- Preserve **per-aggregate ordering per subscriber**: a subscriber never receives an aggregate's event before that aggregate's earlier events (user, 2026-11-24). Other aggregates keep flowing.
- Run on Postgres with `FOR UPDATE SKIP LOCKED`, so several workers (or several app instances) can drain the queue concurrently.
- Keep SQLite for most tests and local development; run a focused set of tests on real Postgres through Testcontainers (user, 2026-11-24).
- Let runtime agents register and remove webhooks through actions: `create_webhook_subscription`, `delete_webhook_subscription`, `list_webhook_subscriptions` (user chose "B, kept simple until auth", 2026-11-24).

## Non-goals

- **A concrete broker adapter.** The template ships the `IEventPublisher` seam and a test fake, not Kafka, RabbitMQ or Service Bus. Each clone adds the one it needs.
- **Webhook management beyond create, list and delete** until auth lands: no pause, update, secret rotation (delete and recreate instead) or web UI.
- **Replay of past events** to a new subscriber. A subscriber sees events committed after it exists. (Approach B below would have given replay; the user chose isolation over replay.)
- **Exactly-once delivery.** Receivers dedupe on the delivery id.
- **Strict ordering across aggregates** or a global order per subscriber.
- **An inbox** (dedupe on the receiving side inside this app).

## Anti-goals

- **A delivery row without the aggregate change, or the reverse.** Atomicity is the point.
- **A preview, stale execute or failed execute that writes delivery rows.** Same rule as history.
- **One bad receiver stalling unrelated work.** A failing subscriber blocks only its own rows for that aggregate.
- **An open relay.** With auth deferred, any MCP caller can create a webhook. Without a host allowlist this is a data-exfiltration path. Webhooks must be off unless an allowlist is configured.
- **A quiet green build.** `VERIFY OK` must never hide that the Postgres tests did not run.
- **Provider SQL leaking.** `FOR UPDATE SKIP LOCKED` stays in `src/AppTemplate.Api/Data` (golden rule 6).

## Constraints

- AGENTS.md golden rules hold. Webhook subscriptions are an aggregate whose changes go through actions, raise `[Event]`s and get history like any other.
- `History` stays append-only and is never updated. Delivery state lives in a new table.
- `StateChangeTests` allows exactly one save, in `ActionPipeline`. Outbox rows are not aggregates, but the worker must update and delete them. The test's allowlist gains the outbox store in `Data/` (one named class), with a comment saying outbox rows are delivery bookkeeping, not business state.
- This development machine has no Docker. Postgres tests cannot run here.
- EF migrations are provider-specific, so supporting both providers means two migration sets.

## Approaches considered

**A. Fan-out at write: one delivery row per (event, subscriber).** Chosen (user, 2026-11-24). In the execute transaction the pipeline resolves the matching subscribers and inserts one `OutboxDelivery` row each. Workers claim rows with `SKIP LOCKED`, skipping any row whose (subscriber, aggregate) has an earlier unfinished row. Gains: independent retries, ordering scoped to the aggregate, the queue the user described. Costs: rows = events × subscribers; a subscriber created later sees only later events; the claim query is subtle and needs real-Postgres tests.

**B. History as the log, with a cursor per subscriber.** No outbox table; each subscriber keeps the last `HistoryEntry.Id` it handled and workers lock cursor rows with `SKIP LOCKED`. Gains: little new schema, global order, replay for new subscribers. Costs: one stuck event blocks the subscriber's whole stream, parallelism only across subscribers, retry state needs a home anyway. Not chosen: isolation matters more than replay.

**C. One row per event, fanned out by the worker at delivery time.** Dropped: retries are coupled across subscribers, which contradicts independent retries.

## Design

### 1. Database providers

- The provider is chosen by configuration: `Database:Provider` = `sqlite` (default) or `postgres`. `Program.cs` registers `UseSqlite` or `UseNpgsql` accordingly. *Builder's call:* the exact key name.
- Two migration sets: `Data/Migrations/Sqlite` and `Data/Migrations/Postgres`, each with its own `DbContext` subclass (`SqliteAppDbContext`, `PostgresAppDbContext`) or migrations assembly, following the EF Core docs on multiple providers. Look this up on Microsoft Learn before choosing. Every schema change from now on is generated for both; the `ef-core` skill and AGENTS.md Commands table say so.
- The Postgres set starts with a new initial migration covering the existing tables (`Projects`, tasks, `History`). The `NOCASE` collation on `ProjectName` becomes `citext` or an ICU collation on Postgres, as `skill:swap-to-postgres` describes. That skill is reused for this part but SQLite is kept, not removed.
- **AppHost stays on SQLite by default** (*my call*: this machine has no Docker). A Postgres AppHost mode (`AddPostgres(...).AddDatabase("appdb")`) is selected by an AppHost parameter or launch profile. Production is expected to run Postgres.

### 2. Subscribers

A subscriber has a stable string key, stored on every delivery row:

| Kind | Key | Matches |
|---|---|---|
| Handler | `handler:<stable name>` | events whose CLR type it handles |
| Publisher | `publisher:<stable name>` | all events, or a filter it declares |
| Webhook | `webhook:<subscription id>` | the subscription's event-name filter |

- *My call:* handlers and publishers declare their stable name with a `[Subscriber("project-indexer")]` attribute rather than using the CLR type name, so renaming a class does not orphan pending rows. An architecture test checks names are present, unique and match `^[a-z0-9-]+$`.
- `IDomainEventHandler<T>` keeps its signature. It now runs in the worker, in a fresh DI scope per delivery, not in the request. A handler that changes state calls an action through `ActionRunner` (golden rule 10); that execute writes its own history and delivery rows.
- **Handlers must be idempotent** (*my call*). Delivery is at least once, so a handler can see an event twice after a lease expiry or a retry. The signature stays the same; a scoped `OutboxDeliveryContext` (delivery id, attempt) is injectable into the handler, so it can dedupe without an interface change. A handler that calls `ActionRunner` passes the delivery id in the `reason` or relies on the action being a no-op the second time (a no-op execute writes nothing). AGENTS.md and `skill:tdd` state the rule, and the failing fixture handler's retry test checks a redelivered event.
- `IEventPublisher` is new: `Task Publish(OutboxEnvelope envelope, CancellationToken ct)`. The envelope is the same one webhooks send (§5).
- `DomainEventDispatcher` and the in-process dispatch after commit are removed. The pipeline remarks and the dispatcher's "no outbox" comments are updated.

### 3. Writing deliveries

In `ActionPipeline.ExecuteCoreAsync`, right after `db.History.AddRange(...)` and before `SaveChangesAsync`:

1. For each raised event, resolve matching subscribers: handlers and publishers from DI (cached per event type), and active webhook subscriptions whose filter includes the event's `[Event]` name. One query for webhook subscriptions per execute, only if any event was raised.
2. Add one `OutboxDelivery` per (event, subscriber).

`OutboxDelivery` columns:

| Column | Notes |
|---|---|
| `Id` | `long`, store-generated, monotonic. Also the delivery id receivers dedupe on |
| `HistoryEntryId` | FK to `History.Id`. Set through a navigation so EF fixes up the generated id in the same save. The payload, event name, aggregate and `traceId` come from the history row; nothing is copied |
| `Subscriber` | the key from §2 |
| `AggregateType`, `AggregateId` | copied from the history row so the ordering check needs no join |
| `Status` | `pending`, `in_flight`, `done`, `dead` |
| `Attempts`, `NextAttemptAt`, `LeaseUntil`, `LastError`, `CompletedAt` | retry and lease bookkeeping. `LastError` truncated to a fixed length |

Index: `(Subscriber, AggregateType, AggregateId, Id)` for the ordering check, and `(Status, NextAttemptAt)` for the claim.

### 4. Claiming and delivering

`OutboxWorker` is a `BackgroundService`. *Builder's call:* worker count per instance, batch size and poll interval, all in an `OutboxOptions` section, with defaults around 1 worker, 20 rows and 1 s.

The claim is one statement in `Data/`, behind `IOutboxStore.ClaimAsync(batchSize, leaseDuration, now)`:

- **Postgres:** `UPDATE ... SET Status='in_flight', LeaseUntil=@lease, Attempts=Attempts+1 WHERE Id IN (SELECT Id FROM outbox_deliveries d WHERE <claimable> ORDER BY Id LIMIT @n FOR UPDATE SKIP LOCKED) RETURNING ...`.
- **SQLite:** the same statement without `FOR UPDATE SKIP LOCKED`. SQLite allows one writer at a time, so it is safe.
- **Claimable:** (`Status='pending'` and `NextAttemptAt <= now`) or (`Status='in_flight'` and `LeaseUntil < now`, which recovers rows from a crashed worker), **and** no row exists with the same `Subscriber`, `AggregateType`, `AggregateId`, a lower `Id` and `Status` in (`pending`, `in_flight`).

The ordering check is safe under concurrency: a worker that skips a locked earlier row still sees it as unfinished in the `NOT EXISTS` check, so the later row stays blocked.

Delivering one row: load the history row, build the envelope, call the subscriber with a timeout, then:

- **Success:** `Status='done'`, `CompletedAt=now`.
- **Failure:** `Status='pending'`, `NextAttemptAt = now + backoff(Attempts)`, `LastError` set. Exponential backoff, *my call*: 10 s base, doubling, capped at 1 h.
- **Out of attempts** (*my call*: 10): `Status='dead'`. **A dead row unblocks the aggregate's later rows** (*my call*: otherwise one poison event stops that aggregate for that subscriber forever). Dead rows stay visible (§6) and are logged at Warning.
- **Subscriber gone:** a deleted webhook subscription or a handler no longer registered. The row is marked `dead` with `LastError` saying why; it is not retried.
- **Event version gone** (handlers only): the worker maps `(EventType, EventVersion)` to the CLR type through a registry built from `[Event]`. If the row's version no longer matches the current type (a payload change bumped it after the row was written), the row is marked `dead` with `LastError` saying so. It is never deserialized best-effort (*my call*). Publishers and webhooks pass the stored payload through unchanged, so they are not affected. Draining the outbox before deploying a version bump avoids this; AGENTS.md says so next to the "bump its version" rule.
- **Retention:** the worker deletes `done` rows older than `OutboxOptions.RetentionDays` (*my call*: 7). Dead rows are kept.

All status updates and deletes go through `IOutboxStore`, the single class `StateChangeTests` allows besides the pipeline.

### 5. Webhooks

**Aggregate `WebhookSubscription`** in `src/AppTemplate.Domain/Webhooks/`:

- `WebhookSubscriptionId`, `WebhookUrl` (VO: absolute `https` URL, or `http` only for loopback hosts), `EventFilter` (VO: a non-empty set of known `[Event]` names, or `*`), `Status` (`Active`, `Deleted`), `CreatedAt`.
- Events: `webhook_subscription.created` v1, `webhook_subscription.deleted` v1.
- Delete is a soft delete, so history stays meaningful and pending deliveries are marked dead by the worker.

**Actions and queries** (the full `skill:add-action` flow each, with `[Covers]` tests):

- `create_webhook_subscription(url, events)`. The result includes the signing secret, shown once. Errors: `webhook.host_not_allowed` (Forbidden) when the URL's host is not in `Webhooks:AllowedHosts`; `webhook.disabled` (Precondition) when the allowlist is empty; `validation.failed` for a bad URL or an unknown event name. The `AllowedHosts` list is loaded by the action and passed into the domain method as data, like name uniqueness.
- `delete_webhook_subscription(subscriptionId)`. Errors: `webhook.not_found`. Marked `Destructive`.
- `list_webhook_subscriptions()`: id, URL, filter, created time. Never the secret.

**Signing secret.** *My call:* the secret is derived, not stored: `HMAC-SHA256(Webhooks:SigningKey, subscriptionId)`. The id is allocated at preview and replayed at execute, so preview and execute agree and nothing secret enters the change set, the preview token or the database. Rotating `Webhooks:SigningKey` changes every secret; in v1 that means recreating subscriptions. The AppHost generates and persists the key the same way it does `preview-signing-key`.

**Delivery.** `POST` to the URL with the JSON envelope:

```json
{
  "id": 123,
  "event": "project.renamed",
  "eventVersion": 1,
  "aggregateType": "Project",
  "aggregateId": "…",
  "aggregateVersion": 4,
  "occurredAt": "2026-11-24T10:00:00Z",
  "traceId": "…",
  "data": { }
}
```

Headers: `Webhook-Id` (the delivery id), `Webhook-Timestamp` (unix seconds), `Webhook-Signature` (`v1,<base64 HMAC-SHA256 of "{id}.{timestamp}.{body}">`). *My call:* this follows the Standard Webhooks spec layout so receivers can use existing verifiers; the builder confirms the exact format against that spec. A named `HttpClient` from `IHttpClientFactory`, 10 s timeout, **no redirects followed**, and the host checked against the allowlist again at delivery time (config may have changed). A 2xx response is success; anything else, or a timeout, is a failure.

### 6. Visibility

- *My call:* one query, `list_outbox_deliveries(status?, subscriber?, aggregateType?, aggregateId?, limit, beforeId)`, newest first, so an agent can see dead or stuck deliveries without reading logs. It returns delivery bookkeeping and the event name, not the payload.
- Telemetry: a span `outbox deliver {subscriber}` per attempt with tags `outbox.subscriber`, `outbox.attempt`, `outbox.outcome`, `event.name`, `aggregate.type`, linked (`ActivityLink`) to the history row's `traceId`. Counters for deliveries by outcome; a gauge or histogram for lag (`now - occurredAt` at success).
- `skill:investigate` gains a step: for a missing side effect, check `list_outbox_deliveries` for the aggregate.

### 7. Testing

**SQLite (the default suite):**

- The worker's hosted service is not started in tests. A test helper, `DrainOutboxAsync()` on `ApiTestBase`, runs claim-and-deliver until no claimable rows remain, using `FakeTimeProvider` to advance through backoff.
- Framework tests use the Notes fixture: `RecordingEventHandler` now receives events through the outbox. Add a failing fixture handler to test retry, backoff, dead, ordering (event 2 is not delivered while event 1 is retrying) and isolation (a failing subscriber does not block another).
- Webhook tests use a fake `HttpMessageHandler` registered on the named client and check the envelope and the signature.
- Atomicity: a stale or failed execute writes no deliveries; a successful one writes exactly events × matching subscribers. Assert the count before looping.
- `WebhookSubscription` actions follow `skill:tdd` with full `[Covers]` coverage.

**Postgres (Testcontainers):**

- A separate project, `tests/AppTemplate.Api.PostgresTests`, using `Testcontainers.PostgreSql` and the Postgres migration set. *My call:* a separate project, so selecting or excluding it needs no trait filter and the main suite never needs Docker.
- It covers: migrations apply cleanly; the claim statement; two concurrent workers never claim the same row; ordering holds under concurrency; lease expiry reclaims an `in_flight` row; one end-to-end execute → delivery.

**`scripts/verify.sh`:**

- Runs the Postgres project when `VERIFY_POSTGRES=1`, and then **fails** if Docker is unavailable.
- Without it, prints `POSTGRES TESTS SKIPPED (set VERIFY_POSTGRES=1)` on its own line before `VERIFY OK`, so a skip is never silent. CI sets `VERIFY_POSTGRES=1`.

### 8. Documentation

AGENTS.md: a new "Outbox and side effects" section (at-least-once delivery, dedupe on delivery id, per-aggregate ordering, dead rows, `list_outbox_deliveries`); the Security section (webhooks are off until `Webhooks:AllowedHosts` is set; this is the guard while auth is deferred); the Commands table (two migration commands, `VERIFY_POSTGRES=1`); the overrides row about Testcontainers (now partly agrees with the kit). `skill:ef-core` (two migration sets), `skill:tdd` (`DrainOutboxAsync`), `skill:investigate`, `skill:remove-example` (the outbox stays; webhooks stay as a framework aggregate, *my call*), `.claude/rules/testing.md`.

## Delivery order

This is three projects that build on each other. Each lands green on its own:

1. **Dual provider.** Provider switch, Postgres migration set, AppHost mode, the Postgres test project with migration tests, and the `verify.sh` step. No outbox yet.
2. **Outbox.** `OutboxDelivery`, the write path, the worker, the claim, handlers moved off the dispatcher, publishers, the visibility query, telemetry and tests on both providers.
3. **Webhooks.** The aggregate, three actions, signing, the delivery client and the allowlist.

## Risks

- **Two migration sets drift.** Mitigation: a test that both model snapshots contain the same tables and columns.
- **Fan-out with many webhooks.** Rows grow with events × subscribers; retention keeps `done` rows bounded. Fine at template scale.
- **Dead rows break the ordering promise** for that aggregate and subscriber. Accepted; they are visible and logged.
- **Handlers are no longer synchronous with the request.** Any code or test that relied on a handler having run when execute returns now needs `DrainOutboxAsync()` or must wait.
- **Exfiltration while auth is deferred.** The host allowlist is the only guard; the default empty list keeps webhooks off.

## Left to the builder

- Option names and defaults marked *Builder's call*, the exact EF multi-provider setup (after a docs lookup), table and column naming per provider, the worker's polling vs `LISTEN/NOTIFY` (polling is enough for v1), and how `DrainOutboxAsync` shares code with the worker.
