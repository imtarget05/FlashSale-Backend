using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpsBridge.Application.Common;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Application.Ingestion;
using OpsBridge.Domain.Contracts;
using OpsBridge.Domain.DLQ;
using OpsBridge.Domain.InboxOutbox;
using OpsBridge.Domain.Ingestion;
using OpsBridge.Infrastructure.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpsBridge.Infrastructure.Messaging;

public sealed class GoodsReceiptConsumerHost : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    public IChannel Channel => _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GoodsReceiptConsumerHost> _logger;
    private const string ConsumerGroup = "goods-receipt-consumer";
    private const int MaxRetryAttempts = 3;

    public GoodsReceiptConsumerHost(
        IConnection connection,
        IChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<GoodsReceiptConsumerHost> logger)
    {
        _connection = connection;
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public static async Task<GoodsReceiptConsumerHost> CreateAsync(
        string connectionString,
        IServiceScopeFactory scopeFactory,
        ILogger<GoodsReceiptConsumerHost> logger)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        await RabbitMqTopology.InitializeTopologyAsync(channel);
        return new GoodsReceiptConsumerHost(connection, channel, scopeFactory, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GoodsReceiptConsumerHost started — subscribing to {Queue} with Manual Ack.", RabbitMqTopology.InboundQueue);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            await HandleMessageAsync(ea, stoppingToken);
        };

        await _channel.BasicConsumeAsync(
            queue: RabbitMqTopology.InboundQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        // Keep service alive until shutdown
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown
        }

        _logger.LogInformation("GoodsReceiptConsumerHost stopping.");
    }

    public async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var deliveryTag = ea.DeliveryTag;
        var rawPayload = Encoding.UTF8.GetString(ea.Body.ToArray());

        Guid eventId;
        string eventType = "GoodsReceiptConfirmed";
        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            eventId = doc.RootElement.GetProperty("eventId").GetGuid();
            if (doc.RootElement.TryGetProperty("eventType", out var et))
            {
                eventType = et.GetString() ?? eventType;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unparseable poison message. Rejecting to DLQ.");
            await RecordDeadLetterAsync(Guid.NewGuid(), "Unknown", RabbitMqTopology.InboundQueue, rawPayload, "Unparseable JSON: " + ex.Message, 0);
            await _channel.BasicRejectAsync(deliveryTag, requeue: false, ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var inboxRepo = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        var eventRepo = scope.ServiceProvider.GetRequiredService<IIngestedEventRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // 1. Consumer-side Deduplication check
        if (await inboxRepo.HasBeenProcessedAsync(eventId, ConsumerGroup, ct))
        {
            _logger.LogInformation("Consumer Inbox: Event {EventId} already processed. Sending idempotent BasicAck.", eventId);
            await _channel.BasicAckAsync(deliveryTag, multiple: false, ct);
            return;
        }

        // 2. Business Execution in DB Transaction
        int retryCount = GetRetryCount(ea.BasicProperties?.Headers);

        try
        {
            // Simulate poison test: if payload contains "FAIL_FORCED_POISON", throw exception
            if (rawPayload.Contains("FAIL_FORCED_POISON", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Simulated poison payload execution failure.");
            }

            await using var tx = await unitOfWork.BeginTransactionAsync(ct);

            // Update IngestedEvent status
            await eventRepo.UpdateStatusAsync(eventId, EventProcessingStatus.Completed, null, ct);

            // Record in Consumer Inbox
            await inboxRepo.AddAsync(new InboxMessage
            {
                MessageId = eventId,
                ConsumerGroup = ConsumerGroup,
                EventType = eventType,
                Source = "goods-receipt-consumer",
                ProcessedAt = DateTimeOffset.UtcNow
            }, ct);

            await tx.CommitAsync(ct);

            // Manual ACK on success
            await _channel.BasicAckAsync(deliveryTag, multiple: false, ct);
            _logger.LogInformation("Event {EventId} successfully processed and ACKed.", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Processing failed for Event {EventId}. Current retry: {Retry}", eventId, retryCount);

            if (retryCount < MaxRetryAttempts)
            {
                // Exponential backoff with jitter
                var backoffMs = (int)(Math.Pow(2, retryCount) * 200 + Random.Shared.Next(50, 150));
                await Task.Delay(backoffMs, ct);

                // Republish with incremented retry header
                var newHeaders = ea.BasicProperties?.Headers != null
                    ? new Dictionary<string, object?>(ea.BasicProperties.Headers)
                    : [];
                newHeaders["x-retry-count"] = retryCount + 1;

                var props = new BasicProperties
                {
                    Persistent = true,
                    Headers = newHeaders
                };

                await _channel.BasicPublishAsync(
                    exchange: RabbitMqTopology.Exchange,
                    routingKey: ea.RoutingKey,
                    mandatory: true,
                    basicProperties: props,
                    body: ea.Body,
                    cancellationToken: ct);

                // ACK old delivery since we safely rescheduled it
                await _channel.BasicAckAsync(deliveryTag, multiple: false, ct);
            }
            else
            {
                // Max retries exceeded: Route to DLQ & record audit
                _logger.LogError("Max retries ({Max}) exceeded for Event {EventId}. Routing to Dead-Letter Queue.", MaxRetryAttempts, eventId);
                await RecordDeadLetterAsync(eventId, eventType, ea.RoutingKey, rawPayload, ex.Message, retryCount);

                // Reject to DLX (requeue = false routes message to Dead-Letter Exchange)
                await _channel.BasicRejectAsync(deliveryTag, requeue: false, ct);
            }
        }
    }

    private async Task RecordDeadLetterAsync(Guid eventId, string eventType, string topic, string payload, string reason, int retries)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();
            db.DeadLetterEvents.Add(new DeadLetterEvent
            {
                EventId = eventId,
                EventType = eventType,
                Topic = topic,
                Payload = payload,
                Reason = reason,
                RetryCount = retries,
                FailedAt = DateTimeOffset.UtcNow,
                IsReplayed = false
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record DeadLetterEvent in database for Event {EventId}", eventId);
        }
    }

    private static int GetRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers == null) return 0;
        if (headers.TryGetValue("x-retry-count", out var val))
        {
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is byte[] b && int.TryParse(Encoding.UTF8.GetString(b), out var parsed)) return parsed;
        }
        return 0;
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
