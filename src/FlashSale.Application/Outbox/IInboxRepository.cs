namespace FlashSale.Application.Outbox;

public interface IInboxRepository
{
    Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
    Task<bool> TryClaimAsync(Guid messageId, string consumerName, CancellationToken ct = default);
    Task MarkProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
}
