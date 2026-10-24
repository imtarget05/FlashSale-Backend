using FlashSale.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

public sealed record OutboxRecoveryResult(string Status, Guid MessageId, string IdempotencyKey);

/// <summary>Atomically revives one dead-lettered event and writes its audit receipt.</summary>
public sealed class OutboxRecoveryService(AppDbContext db)
{
    public async Task<OutboxRecoveryResult> RequeueOneAsync(
        Guid messageId,
        DateTimeOffset expectedDeadLetteredAt,
        string intentId,
        string intentHash,
        string idempotencyKey,
        string approvedBy,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var previous = await db.OutboxRecoveryCommands.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
        if (previous is not null)
        {
            return previous.MessageId == messageId && previous.IntentId == intentId
                   && previous.IntentHash == intentHash && previous.ApprovedBy == approvedBy
                   && IsMatchingTimestamp(previous.ExpectedDeadLetteredAt, expectedDeadLetteredAt)
                ? new OutboxRecoveryResult("already_applied", messageId, idempotencyKey)
                : new OutboxRecoveryResult("idempotency_conflict", messageId, idempotencyKey);
        }

        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "OutboxMessages"
            SET "RetryCount" = 0,
                "NextAttemptAt" = NULL,
                "DeadLetteredAt" = NULL,
                "Error" = NULL,
                "ClaimToken" = NULL,
                "ClaimedUntil" = NULL
            WHERE "MessageId" = {messageId}
              AND "ProcessedAt" IS NULL
              AND "DeadLetteredAt" = {expectedDeadLetteredAt}
              AND ("ClaimedUntil" IS NULL OR "ClaimedUntil" <= now())
            """, ct);

        if (affected != 1)
        {
            await transaction.RollbackAsync(ct);
            // A concurrent retry may have committed the identical command while
            // this transaction waited for the event row lock. Read the durable
            // receipt again after rollback under a fresh Read Committed snapshot.
            var concurrent = await db.OutboxRecoveryCommands.AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
            if (concurrent is not null)
                return concurrent.MessageId == messageId && concurrent.IntentId == intentId
                       && concurrent.IntentHash == intentHash && concurrent.ApprovedBy == approvedBy
                       && IsMatchingTimestamp(concurrent.ExpectedDeadLetteredAt, expectedDeadLetteredAt)
                    ? new OutboxRecoveryResult("already_applied", messageId, idempotencyKey)
                    : new OutboxRecoveryResult("idempotency_conflict", messageId, idempotencyKey);
            var exists = await db.OutboxMessages.AsNoTracking().AnyAsync(x => x.MessageId == messageId, ct);
            return new OutboxRecoveryResult(exists ? "state_conflict" : "not_found", messageId, idempotencyKey);
        }

        db.OutboxRecoveryCommands.Add(new OutboxRecoveryCommand
        {
            IdempotencyKey = idempotencyKey,
            MessageId = messageId,
            IntentId = intentId,
            IntentHash = intentHash,
            ApprovedBy = approvedBy,
            ExpectedDeadLetteredAt = expectedDeadLetteredAt,
            ExecutedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new OutboxRecoveryResult("requeued", messageId, idempotencyKey);
    }

    private static bool IsMatchingTimestamp(DateTimeOffset a, DateTimeOffset b) =>
        Math.Abs((a - b).TotalMilliseconds) < 1;
}
