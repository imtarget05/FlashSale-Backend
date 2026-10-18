using OpsBridge.Domain.InboxOutbox;

namespace OpsBridge.Application.InboxOutbox;

public interface IInboxRepository
{
    Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerGroup = "default", CancellationToken ct = default);
    Task<InboxMessage?> GetMessageAsync(Guid messageId, string consumerGroup = "default", CancellationToken ct = default);
    Task MarkProcessedAsync(Guid messageId, string consumerGroup, string eventType, string source, string payloadHash = "", CancellationToken ct = default);
    Task AddAsync(InboxMessage inboxMessage, CancellationToken ct = default);
}
