using FlashSale.Application.Events;
using FlashSale.Domain.Events;
using FlashSale.Domain.Outbox;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Outbox;

/// <summary>
/// Dispatches pending Outbox messages to the event backbone (Kafka / RabbitMQ).
/// Marks each message processed only after the transport acknowledges receipt.
/// </summary>
public sealed class OutboxDispatcherUseCase(
    IOutboxRepository outboxRepository,
    IDomainEventPublisher publisher,
    ILogger<OutboxDispatcherUseCase> logger)
{
    public async Task<int> ExecuteBatchAsync(int batchSize = 50, CancellationToken ct = default)
    {
        var messages = await outboxRepository.GetUnprocessedAsync(batchSize, ct);
        if (messages.Count == 0)
        {
            return 0;
        }

        var dispatched = 0;
        foreach (var msg in messages)
        {
            try
            {
                var @event = new OutboxDomainEventWrapper(
                    msg.MessageId.ToString("N"),
                    msg.EventType,
                    msg.CreatedAt,
                    msg.MessageId,
                    "outbox-dispatcher",
                    msg.Payload);

                await publisher.PublishAsync(@event, ct);
                await outboxRepository.MarkProcessedAsync(msg.Id, ct);
                dispatched++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to dispatch outbox message {Id} ({Type}).", msg.Id, msg.EventType);
                await outboxRepository.MarkFailedAsync(msg.Id, ex.Message, ct);
            }
        }

        return dispatched;
    }

    private sealed record OutboxDomainEventWrapper(
        string EventId,
        string EventType,
        DateTimeOffset OccurredAt,
        Guid CorrelationId,
        string Source,
        string RawPayload) : DomainEvent(EventId, EventType, OccurredAt, CorrelationId, Source)
    {
        public override object Payload => RawPayload;
    }
}
