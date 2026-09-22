namespace FlashSale.Application.Messaging;

/// <summary>A message received from a queue, plus how to ack/dead-letter it.</summary>
public sealed class QueueEntry
{
    public required OrderMessage Message { get; init; }

    /// <summary>Acknowledge successful processing (Service Bus: complete the lock).</summary>
    public Func<Task>? Complete { get; init; }

    /// <summary>Route to the dead-letter queue (Service Bus native DLQ).</summary>
    public Func<Task>? DeadLetter { get; init; }
}

/// <summary>Port: publish an order for asynchronous fulfillment.</summary>
public interface IOrderQueueProducer
{
    /// <returns>false when the queue is full (backpressure signal for the API).</returns>
    ValueTask<bool> EnqueueAsync(OrderMessage message, CancellationToken ct = default);
}

/// <summary>Port: receive and settle messages (implemented by InMemory/RabbitMQ/ServiceBus).</summary>
public interface IOrderQueueConsumer
{
    /// <returns>null when no message is available within a short window.</returns>
    ValueTask<QueueEntry?> DequeueAsync(CancellationToken ct = default);
}

