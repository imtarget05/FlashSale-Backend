using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using FlashSale.Application.Messaging;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Messaging;

public class RabbitMQOrderQueue : IOrderQueueProducer, IOrderQueueConsumer, IDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly string _queueName;
    private readonly ILogger<RabbitMQOrderQueue> _logger;
    private readonly AsyncEventingBasicConsumer _consumer;
    private readonly System.Threading.Channels.Channel<Delivery> _deliveryChannel;
    // Serializes publishes on this singleton channel (see EnqueueAsync).
    // ONE gate for ALL uses of the shared IChannel — publish AND ack/reject.
    // RabbitMQ.Client channels are single-threaded (the client contract says
    // applications must serialize channel use). The v5 oversell proof still
    // produced a poison message after publish-only serialization: settlement
    // acks from the consumer path interleaved frames with an in-flight
    // publish on this same singleton channel.
    private readonly SemaphoreSlim _channelGate = new(1, 1);

    private RabbitMQOrderQueue(
        IConnection connection,
        IChannel channel,
        string queueName,
        ILogger<RabbitMQOrderQueue> logger,
        AsyncEventingBasicConsumer consumer,
        System.Threading.Channels.Channel<Delivery> deliveryChannel)
    {
        _connection = connection;
        _channel = channel;
        _queueName = queueName;
        _logger = logger;
        _consumer = consumer;
        _deliveryChannel = deliveryChannel;
    }

    /// <summary>
    /// Async factory — avoids sync-over-async (.GetAwaiter().GetResult()) in the
    /// constructor, which can cause thread-pool exhaustion under load.
    /// </summary>
    public static async Task<RabbitMQOrderQueue> CreateAsync(
        string connectionString, string queueName, ILogger<RabbitMQOrderQueue> logger)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(queue: queueName,
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);
        await channel.BasicQosAsync(0, 10, false);

        var deliveryChannel = System.Threading.Channels.Channel.CreateBounded<Delivery>(100);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            // RabbitMQ.Client v7 OWNS the memory behind ea.Body and documents it
            // as valid ONLY inside this handler:
            //   "the ReadOnlyMemory<byte> that represents the message body is
            //    owned by this library, and that memory is only valid for
            //    application use within the context of the executing
            //    ReceivedAsync event ... you MUST copy the data"
            //   (v7-MIGRATION.md; same NOTE on BasicDeliverEventArgs.Body)
            // This queue handles deliveries asynchronously through an in-process
            // channel, so the pooled buffer would be recycled before the JSON
            // read — the observed symptom was a truncated document
            // ($.EventId missing at byte 260) that surfaced as "Poison message"
            // after the handler had already returned. Copy the body here.
            await deliveryChannel.Writer.WriteAsync(
                new Delivery(ea.DeliveryTag, ea.Body.ToArray()));
        };
        await channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer);

        return new RabbitMQOrderQueue(connection, channel, queueName, logger, consumer, deliveryChannel);
    }

    public async ValueTask<bool> EnqueueAsync(OrderMessage message, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);
            var properties = new BasicProperties { Persistent = true };

            // Channel gate (see field comment): this queue is a singleton and
            // the L6 oversell proof hit 10 concurrent producers on ONE channel.
            // Observed symptom: the broker stored messages truncated mid-string
            // ($.EventId, byte 260) with NO exception surfaced to the caller —
            // i.e. frame interleaving on an unsynchronized socket write. One
            // in-flight channel operation at a time makes the contract explicit.
            await _channelGate.WaitAsync(ct);
            try
            {
                await _channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: _queueName,
                    mandatory: true,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: ct);
            }
            finally
            {
                _channelGate.Release();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue to RabbitMQ");
            return false;
        }
    }

    /// <summary>
    /// Run one IChannel operation under the shared channel gate. Settlements
    /// deliberately take no CancellationToken — an ack/reject must complete
    /// even when the caller's token has already fired, or the delivery would
    /// leak unacked again.
    /// </summary>
    // RabbitMQ.Client v7 channel methods (BasicAckAsync/BasicRejectAsync)
    // return ValueTask, so the gate takes Func<ValueTask>.
    private async Task WithChannelGateAsync(Func<ValueTask> operation)
    {
        await _channelGate.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _channelGate.Release();
        }
    }

    /// <summary>
    /// One delivery with an OWNED copy of the body. RabbitMQ.Client v7 owns the
    /// memory behind <c>BasicDeliverEventArgs.Body</c> and documents it as valid
    /// only inside the executing <c>ReceivedAsync</c> handler, so a delivery
    /// that is queued for deferred handling must carry copied bytes.
    /// </summary>
    private sealed record Delivery(ulong DeliveryTag, byte[] Body);

    public async ValueTask<QueueEntry?> DequeueAsync(CancellationToken ct = default)
    {
        Delivery? delivery = null;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            delivery = await _deliveryChannel.Reader.ReadAsync(timeoutCts.Token);
            var json = Encoding.UTF8.GetString(delivery.Body);
            var message = JsonSerializer.Deserialize<OrderMessage>(json);

            if (message == null)
            {
                await WithChannelGateAsync(() =>
                    _channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false));
                return null;
            }

            return new QueueEntry
            {
                Message = message,
                Complete = async () =>
                {
                    await WithChannelGateAsync(() =>
                        _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false));
                },
                DeadLetter = async () =>
                {
                    // For RabbitMQ, deadletter implies reject without requeue
                    // (Assuming a DLX is configured, or just dropping it if not).
                    await WithChannelGateAsync(() =>
                        _channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false));
                }
            };
        }
        catch (OperationCanceledException)
        {
            // No message arrived within 2 seconds
            return null;
        }
        catch (JsonException ex)
        {
            // Poison delivery: the body cannot round-trip into OrderMessage
            // (observed with truncated frames from unsynchronized concurrent
            // publishes). Without this reject the delivery stays unacked
            // forever, prefetch slots leak, and the harness stalls at
            // settled < accepted. Drop it — a malformed order can never be
            // processed, and requeueing would loop on the same bytes.
            _logger.LogError(ex, "Poison message on {Queue} — rejecting without requeue.", _queueName);
            if (delivery is not null)
            {
                try
                {
                    await WithChannelGateAsync(() =>
                        _channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false));
                }
                catch (Exception rejectEx)
                {
                    // Connection may already be gone; the broker will requeue
                    // on channel close either way.
                    _logger.LogWarning(rejectEx, "Could not reject poison delivery on {Queue}.", _queueName);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from RabbitMQ");
            return null;
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
