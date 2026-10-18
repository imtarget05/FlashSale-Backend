namespace OpsBridge.Application.InboxOutbox;

public interface IInboxRepository
{
    Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerGroup = "default", CancellationToken ct = default);
    Task MarkProcessedAsync(Guid messageId, string consumerGroup, string eventType, string source, CancellationToken ct = default);
}
