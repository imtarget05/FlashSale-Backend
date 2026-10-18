using OpsBridge.Domain.InboxOutbox;

namespace OpsBridge.Application.InboxOutbox;

public interface IConsumerProcessingRepository
{
    Task<bool> HasBeenProcessedAsync(Guid eventId, string consumerGroup, CancellationToken ct = default);
    Task<ConsumerProcessingState?> GetAsync(Guid eventId, string consumerGroup, CancellationToken ct = default);
    Task RecordAsync(ConsumerProcessingState state, CancellationToken ct = default);
}
