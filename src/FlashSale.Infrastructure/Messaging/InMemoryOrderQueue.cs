using System.Threading.Channels;
using FlashSale.Application.Messaging;
using FlashSale.Domain.Messaging;

namespace FlashSale.Infrastructure.Messaging;

/// <summary>
/// Dev-mode queue: a bounded in-process channel. Producers get natural
/// backpressure (TryWrite fails when full -> API answers 503). Producer and
/// consumer MUST resolve to the SAME instance (registered as one singleton).
/// </summary>
public sealed class InMemoryOrderQueue : IOrderQueueProducer, IOrderQueueConsumer
{
    private readonly Channel<OrderMessage> _channel =
        Channel.CreateBounded<OrderMessage>(new BoundedChannelOptions(5_000)
        {
            SingleReader = false,
            SingleWriter = false
        });

    public ValueTask<bool> EnqueueAsync(OrderMessage message, CancellationToken ct = default) =>
        ValueTask.FromResult(_channel.Writer.TryWrite(message));

    public async ValueTask<QueueEntry?> DequeueAsync(CancellationToken ct = default)
    {
        var message = await _channel.Reader.ReadAsync(ct);
        // In-process: completion is implicit; retries re-enqueue a copy.
        return new QueueEntry { Message = message };
    }
}
