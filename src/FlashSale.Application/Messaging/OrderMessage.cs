namespace FlashSale.Application.Messaging;

public sealed record OrderMessage(
    int ProductId,
    int Quantity,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);

public interface IOrderQueueProducer
{
    ValueTask<bool> EnqueueAsync(OrderMessage message, CancellationToken ct = default);
}

public interface IOrderQueueConsumer
{
    ValueTask<QueueEntry?> DequeueAsync(CancellationToken ct = default);
}

public sealed record QueueEntry
{
    public OrderMessage Message { get; init; } = null!;
    public Func<CancellationToken, Task> Complete { get; init; } = _ => Task.CompletedTask;
}
