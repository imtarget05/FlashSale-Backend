namespace FlashSale.Application.Outbox;

using FlashSale.Domain.Outbox;
using FlashSale.Application.Persistence;
using Microsoft.Extensions.Logging;

public sealed class OutboxEventWriter
{
    private readonly IOutboxRepository _outbox;

    public OutboxEventWriter(IOutboxRepository outbox) => _outbox = outbox;

    public Task EnqueueAsync(
        string eventType,
        string aggregateType,
        string aggregateId,
        Guid correlationId,
        string payload,
        CancellationToken ct = default)
    {
        var message = new OutboxMessage(
            Guid.NewGuid(),
            eventType,
            aggregateType,
            aggregateId,
            correlationId,
            payload,
            DateTimeOffset.UtcNow);
        return _outbox.EnqueueAsync(message, ct);
    }
}
