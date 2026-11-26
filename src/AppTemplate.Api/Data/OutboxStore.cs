using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Data;

/// <summary>
/// The only code besides the action pipeline that changes outbox rows (<c>StateChangeTests</c> allows it by name).
/// Outbox rows are delivery bookkeeping, not business state; nothing here touches an aggregate or a history row.
/// Provider-specific SQL lives here and nowhere else.
/// </summary>
public sealed class OutboxStore(AppDbContext db)
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> claimable rows, oldest first, in one statement: sets them in flight
    /// until <paramref name="leaseUntil"/> and counts the attempt. A row is claimable when it is pending and due, or
    /// in flight with an expired lease, and no earlier row for the same subscriber and aggregate is still pending or
    /// in flight. On Postgres, rows another claimer holds are skipped, not waited for.
    /// </summary>
    public async Task<IReadOnlyList<long>> ClaimAsync(int batchSize, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken ct)
    {
        // SKIP LOCKED needs FOR UPDATE, which only Postgres has; SQLite serializes writers, so it needs neither.
        // The NOT EXISTS reads unlocked rows, so a skipped earlier row still blocks its later rows.
        var ids = db.Database.IsNpgsql()
            ? db.Database.SqlQuery<long>($"""
                UPDATE "OutboxDeliveries" SET "Status" = 'InFlight', "LeaseUntil" = {leaseUntil}, "Attempts" = "Attempts" + 1
                WHERE "Id" IN (
                    SELECT d."Id" FROM "OutboxDeliveries" d
                    WHERE ((d."Status" = 'Pending' AND d."NextAttemptAt" <= {now}) OR (d."Status" = 'InFlight' AND d."LeaseUntil" < {now}))
                      AND NOT EXISTS (
                        SELECT 1 FROM "OutboxDeliveries" e
                        WHERE e."Subscriber" = d."Subscriber" AND e."AggregateType" = d."AggregateType" AND e."AggregateId" = d."AggregateId"
                          AND e."Id" < d."Id" AND e."Status" IN ('Pending', 'InFlight'))
                    ORDER BY d."Id"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED)
                RETURNING "Id" AS "Value"
                """)
            : db.Database.SqlQuery<long>($"""
                UPDATE "OutboxDeliveries" SET "Status" = 'InFlight', "LeaseUntil" = {leaseUntil}, "Attempts" = "Attempts" + 1
                WHERE "Id" IN (
                    SELECT d."Id" FROM "OutboxDeliveries" d
                    WHERE ((d."Status" = 'Pending' AND d."NextAttemptAt" <= {now}) OR (d."Status" = 'InFlight' AND d."LeaseUntil" < {now}))
                      AND NOT EXISTS (
                        SELECT 1 FROM "OutboxDeliveries" e
                        WHERE e."Subscriber" = d."Subscriber" AND e."AggregateType" = d."AggregateType" AND e."AggregateId" = d."AggregateId"
                          AND e."Id" < d."Id" AND e."Status" IN ('Pending', 'InFlight'))
                    ORDER BY d."Id"
                    LIMIT {batchSize})
                RETURNING "Id" AS "Value"
                """);

        var claimed = await ids.ToListAsync(ct);
        claimed.Sort();
        return claimed;
    }

    /// <summary>The claimed rows with their history entries, oldest first, untracked.</summary>
    public async Task<IReadOnlyList<OutboxDelivery>> LoadAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
        => await db.OutboxDeliveries.AsNoTracking().Include(d => d.HistoryEntry)
            .Where(d => ids.Contains(d.Id)).OrderBy(d => d.Id).ToListAsync(ct);

    // Each settle is guarded by the attempt it claimed: a worker whose lease expired and whose row was reclaimed
    // cannot overwrite the newer attempt's outcome.

    /// <summary>
    /// Extends a claimed row's lease just before its delivery starts. 0 means the lease was lost (another worker
    /// reclaimed the row after it expired while it waited in this batch): skip it.
    /// </summary>
    public Task<int> RenewLeaseAsync(long id, int attempt, DateTimeOffset leaseUntil, CancellationToken ct)
        => InFlight(id, attempt).ExecuteUpdateAsync(s => s.SetProperty(d => d.LeaseUntil, leaseUntil), ct);

    public Task<int> CompleteAsync(long id, int attempt, DateTimeOffset now, CancellationToken ct)
        => InFlight(id, attempt).ExecuteUpdateAsync(
            s => s.SetProperty(d => d.Status, OutboxStatus.Done).SetProperty(d => d.CompletedAt, now).SetProperty(d => d.LeaseUntil, (DateTimeOffset?)null),
            ct);

    public Task<int> FailAsync(long id, int attempt, string error, DateTimeOffset nextAttemptAt, CancellationToken ct)
        => InFlight(id, attempt).ExecuteUpdateAsync(
            s => s.SetProperty(d => d.Status, OutboxStatus.Pending)
                .SetProperty(d => d.NextAttemptAt, nextAttemptAt)
                .SetProperty(d => d.LastError, Truncate(error))
                .SetProperty(d => d.LeaseUntil, (DateTimeOffset?)null),
            ct);

    public Task<int> MarkDeadAsync(long id, int attempt, string error, CancellationToken ct)
        => InFlight(id, attempt).ExecuteUpdateAsync(
            s => s.SetProperty(d => d.Status, OutboxStatus.Dead).SetProperty(d => d.LastError, Truncate(error)).SetProperty(d => d.LeaseUntil, (DateTimeOffset?)null),
            ct);

    /// <summary>Deletes delivered rows completed before <paramref name="cutoff"/>. Dead rows are kept.</summary>
    /// <remarks>Raw SQL: EF's SQLite provider cannot translate <see cref="DateTimeOffset"/> comparisons.</remarks>
    public Task<int> DeleteDoneBeforeAsync(DateTimeOffset cutoff, CancellationToken ct)
        => db.Database.ExecuteSqlAsync(
            $"""DELETE FROM "OutboxDeliveries" WHERE "Status" = 'Done' AND "CompletedAt" < {cutoff}""", ct);

    private IQueryable<OutboxDelivery> InFlight(long id, int attempt)
        => db.OutboxDeliveries.Where(d => d.Id == id && d.Status == OutboxStatus.InFlight && d.Attempts == attempt);

    private static string Truncate(string error)
        => error.Length <= OutboxDelivery.LastErrorMaxLength ? error : error[..OutboxDelivery.LastErrorMaxLength];
}
