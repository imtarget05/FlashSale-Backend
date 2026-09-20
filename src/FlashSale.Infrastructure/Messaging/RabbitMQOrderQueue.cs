using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using FlashSale.Application.Messaging;
using FlashSale.Domain.Messaging;
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

    public RabbitMQOrderQueue(string connectionString, string queueName, ILogger<RabbitMQOrderQueue> logger)
    {
        _queueName = queueName;
        _logger = logger;

        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

        _channel.QueueDeclareAsync(queue: _queueName,
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null).GetAwaiter().GetResult();
        _channel.BasicQosAsync(0, 10, false).GetAwaiter().GetResult();

        _deliveryChannel = System.Threading.Channels.Channel.CreateBounded<BasicDeliverEventArgs>(100);

        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.ReceivedAsync += async (model, ea) =>
        {
            await _deliveryChannel.Writer.WriteAsync(ea);
        };
        _channel.BasicConsumeAsync(queue: _queueName, autoAck: false, consumer: _consumer).GetAwaiter().GetResult();
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
