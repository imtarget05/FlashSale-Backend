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
    private readonly System.Threading.Channels.Channel<BasicDeliverEventArgs> _deliveryChannel;

    private RabbitMQOrderQueue(
        IConnection connection,
        IChannel channel,
        string queueName,
        ILogger<RabbitMQOrderQueue> logger,
        AsyncEventingBasicConsumer consumer,
        System.Threading.Channels.Channel<BasicDeliverEventArgs> deliveryChannel)
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

        var deliveryChannel = System.Threading.Channels.Channel.CreateBounded<BasicDeliverEventArgs>(100);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            await deliveryChannel.Writer.WriteAsync(ea);
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

            await _channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _queueName,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue to RabbitMQ");
            return false;
        }
    }

    public async ValueTask<QueueEntry?> DequeueAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            var ea = await _deliveryChannel.Reader.ReadAsync(timeoutCts.Token);
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var message = JsonSerializer.Deserialize<OrderMessage>(json);

            if (message == null)
            {
                await _channel.BasicRejectAsync(ea.DeliveryTag, requeue: false);
                return null;
            }

            return new QueueEntry
            {
                Message = message,
                Complete = async () =>
                {
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                },
                DeadLetter = async () =>
                {
                    // For RabbitMQ, deadletter implies reject without requeue
                    // (Assuming a DLX is configured, or just dropping it if not).
                    await _channel.BasicRejectAsync(ea.DeliveryTag, requeue: false);
                }
            };
        }
        catch (OperationCanceledException)
        {
            // No message arrived within 2 seconds
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
