using Azure.Messaging.ServiceBus;
using FlashSale.Application.Messaging;
using FlashSale.Domain.Messaging;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Messaging;

/// <summary>
/// Azure production queue (ADR-005): Azure Service Bus in PeekLock mode.
/// Same port contract as RabbitMQ/InMemory — JSON <see cref="OrderMessage"/>,
/// at-least-once delivery, idempotent consumer, Complete on success,
/// DeadLetter routes to the native Service Bus DLQ. Never throws for a
/// missing message: returns null so the host loop keeps polling.
/// </summary>
public sealed class ServiceBusOrderQueue : IOrderQueueProducer, IOrderQueueConsumer, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly ServiceBusReceiver _receiver;
    private readonly ILogger<ServiceBusOrderQueue> _logger;

    public ServiceBusOrderQueue(string connectionString, string queueName, ILogger<ServiceBusOrderQueue> logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Service Bus connection string is required.", nameof(connectionString));
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name is required.", nameof(queueName));
        _logger = logger;

        _client = new ServiceBusClient(connectionString);
        _sender = _client.CreateSender(queueName);
        _receiver = _client.CreateReceiver(queueName, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = 10
        });
    }

    public async ValueTask<bool> EnqueueAsync(OrderMessage message, CancellationToken ct = default)
    {
        try
        {
            var body = message.ToJson();
            var sbMessage = new ServiceBusMessage(body)
            {
                MessageId = message.IdempotencyKey,
                ContentType = "application/json"
            };
            await _sender.SendMessageAsync(sbMessage, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue order {Key} to Service Bus.", message.IdempotencyKey);
            return false;
        }
    }

    public async ValueTask<QueueEntry?> DequeueAsync(CancellationToken ct = default)
    {
        ServiceBusReceivedMessage? received;
        try
        {
            received = await _receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2), ct);
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        if (received is null)
            return null;

        OrderMessage message;
        try
        {
            message = OrderMessage.FromJson(received.Body.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unreadable Service Bus payload; dead-lettering.");
            await _receiver.DeadLetterMessageAsync(received, cancellationToken: CancellationToken.None);
            return null;
        }

        return new QueueEntry
        {
            Message = message,
            Complete = async () => await _receiver.CompleteMessageAsync(received),
            DeadLetter = async () => await _receiver.DeadLetterMessageAsync(received)
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _receiver.DisposeAsync();
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}
