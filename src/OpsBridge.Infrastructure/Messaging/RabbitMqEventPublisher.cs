using System.Text;
using Microsoft.Extensions.Logging;
using OpsBridge.Application.Messaging;
using RabbitMQ.Client;

namespace OpsBridge.Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private RabbitMqEventPublisher(
        IConnection connection,
        IChannel channel,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connection = connection;
        _channel = channel;
        _logger = logger;
    }

    public static async Task<RabbitMqEventPublisher> CreateAsync(
        string connectionString,
        ILogger<RabbitMqEventPublisher> logger)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        var connection = await factory.CreateConnectionAsync();

        // Enable Publisher Confirmations
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true
        );
        var channel = await connection.CreateChannelAsync(channelOptions);

        await RabbitMqTopology.InitializeTopologyAsync(channel);

        return new RabbitMqEventPublisher(connection, channel, logger);
    }

    public async Task<bool> PublishAsync(
        string topic,
        string payload,
        IDictionary<string, object?>? headers = null,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var body = Encoding.UTF8.GetBytes(payload);
            var props = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent
            };

            if (headers != null)
            {
                props.Headers = new Dictionary<string, object?>(headers);
            }

            // Publish to RabbitMQ exchange
            await _channel.BasicPublishAsync(
                exchange: RabbitMqTopology.Exchange,
                routingKey: topic,
                mandatory: true,
                basicProperties: props,
                body: body,
                cancellationToken: ct);

            _logger.LogDebug("Published event to topic {Topic} with Publisher Confirmations.", topic);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to topic {Topic}", topic);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
