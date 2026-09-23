using Confluent.Kafka;
using FlashSale.Application.Events;
using FlashSale.Domain.Events;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace FlashSale.Infrastructure.Events;

/// <summary>
/// Kafka publisher for transactional outbox / domain events (Phase 10).
/// Partition key uses CorrelationId for strict per-order or per-saga ordering.
/// Confluent.Kafka Producer is thread-safe and internally pools buffers.
/// </summary>
public sealed class KafkaDomainEventPublisher : IDomainEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly ILogger<KafkaDomainEventPublisher> _logger;

    public KafkaDomainEventPublisher(
        string bootstrapServers,
        ILogger<KafkaDomainEventPublisher> logger,
        string topic = "orders.events")
    {
        _logger = logger;
        _topic = topic;

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All, // Strong durability for transactional events
            EnableIdempotence = true, // Prevent duplicate writes at the transport level
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(DomainEvent domainEvent, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
            var message = new Message<string, string>
            {
                Key = domainEvent.CorrelationId != Guid.Empty ? domainEvent.CorrelationId.ToString() : domainEvent.EventId,
                Value = json,
                Headers = new Headers
                {
                    { "event-type", Encoding.UTF8.GetBytes(domainEvent.EventType) },
                    { "correlation-id", Encoding.UTF8.GetBytes(domainEvent.CorrelationId.ToString()) },
                    { "event-id", Encoding.UTF8.GetBytes(domainEvent.EventId) }
                }
            };

            var deliveryResult = await _producer.ProduceAsync(_topic, message, ct);
            _logger.LogInformation(
                "Produced event {EventType} ({EventId}) to Kafka [{Topic}] partition {Partition} at offset {Offset}.",
                domainEvent.EventType,
                domainEvent.EventId,
                _topic,
                deliveryResult.Partition.Value,
                deliveryResult.Offset.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to produce {EventType} ({EventId}) to Kafka topic {Topic}.",
                domainEvent.EventType, domainEvent.EventId, _topic);
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
