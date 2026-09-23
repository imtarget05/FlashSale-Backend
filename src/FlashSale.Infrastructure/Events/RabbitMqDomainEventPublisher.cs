using FlashSale.Application.Events;
using FlashSale.Domain.Events;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace FlashSale.Infrastructure.Events;

/// <summary>
/// RabbitMQ publisher for domain/automation events (spec §1): topic exchange
/// "automation.events", routing key = event type, headers carry event-type and
/// correlation-id. JSON is the flat typed event (every envelope field is a
/// record ctor param), so consumers round-trip with one typed deserialize.
/// Async factory mirrors RabbitMQOrderQueue.CreateAsync — no sync-over-async.
/// </summary>
public sealed class RabbitMqDomainEventPublisher : IDomainEventPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly ILogger<RabbitMqDomainEventPublisher> _logger;
    private readonly string _exchangeName;

    private RabbitMqDomainEventPublisher(IConnection connection, IChannel channel,
        ILogger<RabbitMqDomainEventPublisher> logger, string exchangeName)
    {
        _connection = connection;
        _channel = channel;
        _logger = logger;
        _exchangeName = exchangeName;
    }

    public static async Task<RabbitMqDomainEventPublisher> CreateAsync(
        string connectionString,
        ILogger<RabbitMqDomainEventPublisher> logger,
        string exchangeName = "automation.events")
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Topic, durable: true);
        return new RabbitMqDomainEventPublisher(connection, channel, logger, exchangeName);
    }

    public async Task PublishAsync(DomainEvent domainEvent, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
            var properties = new BasicProperties
            {
                Persistent = true,
                MessageId = domainEvent.EventId,
                ContentType = "application/json",
                Headers = new Dictionary<string, object>
                {
                    ["event-type"] = domainEvent.EventType,
                    ["correlation-id"] = domainEvent.CorrelationId.ToString()
                }
            };
            await _channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: domainEvent.EventType,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(json),
                mandatory: true,
                cancellationToken: ct);
            _logger.LogDebug("Published {EventType} {EventId} to {Exchange}.",
                domainEvent.EventType, domainEvent.EventId, _exchangeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish {EventType} {EventId}.",
                domainEvent.EventType, domainEvent.EventId);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _channel.DisposeAsync(); } catch { /* shutdown race is non-fatal */ }
        try { await _connection.DisposeAsync(); } catch { }
    }
}