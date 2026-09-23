using FlashSale.Domain.Outbox;

namespace FlashSale.Application.Outbox;

/// <summary>
/// Port for Transactional Outbox persistence and retrieval.
/// </summary>
public interface IOutboxRepository
{
    Task EnqueueAsync(OutboxMessage message, CancellationToken ct = default);

    /// <summary>
    /// Rows eligible RIGHT NOW: not processed, not dead-lettered, and past
    /// their backoff deadline (NextAttemptAt is null or already due).
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize = 50, CancellationToken ct = default);

    Task MarkProcessedAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Records a failed attempt: increments RetryCount, applies exponential
    /// backoff to NextAttemptAt, and dead-letters the row once
    /// <see cref="FlashSale.Domain.Outbox.OutboxMessage.MaxRetryCount"/> is reached.
    /// </summary>
    Task MarkFailedAsync(long id, string error, CancellationToken ct = default);

    /// <summary>
    /// Rows that are NOT progressing: dead-lettered, or waiting on a backoff
    /// deadline. This is the honest counterpart to pending — a row stuck at
    /// the retry ceiling must never be reported as "no pending messages".
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> GetStuckAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>Count only, for cheap health/metrics probes.</summary>
    Task<int> CountStuckAsync(CancellationToken ct = default);

    /// <summary>
    /// Operator recovery: clears the retry/backoff/dead-letter state so the
    /// dispatcher picks the rows up again. Returns rows revived.
    /// </summary>
    Task<int> RequeueStuckAsync(CancellationToken ct = default);
}
