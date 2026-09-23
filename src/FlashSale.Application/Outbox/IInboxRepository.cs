namespace FlashSale.Application.Outbox;

/// <summary>
/// Port for Deduplication Inbox repository (Phase 10).
/// </summary>
public interface IInboxRepository
{
    Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
    Task MarkProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
}
