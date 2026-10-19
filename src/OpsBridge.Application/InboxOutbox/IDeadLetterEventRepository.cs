using OpsBridge.Domain.DLQ;

namespace OpsBridge.Application.InboxOutbox;

public interface IDeadLetterEventRepository
{
    Task<IReadOnlyList<DeadLetterEvent>> GetRecentAsync(int limit, CancellationToken ct = default);
    Task<DeadLetterReplayResult> QueueReplayAsync(Guid eventId, string actor, CancellationToken ct = default);
}

public enum DeadLetterReplayStatus
{
    NotFound,
    AlreadyReplayed,
    Queued
}

public sealed record DeadLetterReplayResult(
    DeadLetterReplayStatus Status,
    Guid EventId,
    Guid? OutboxId = null,
    DateTimeOffset? ReplayedAt = null,
    string? ReplayedBy = null);