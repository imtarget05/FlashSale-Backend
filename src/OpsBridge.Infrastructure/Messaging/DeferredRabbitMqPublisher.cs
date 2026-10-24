using Microsoft.Extensions.Logging;
using OpsBridge.Application.Messaging;

namespace OpsBridge.Infrastructure.Messaging;

/// <summary>
/// Opens the broker connection asynchronously on the first outbox publish.
/// This avoids sync-over-async inside the DI container. A failed connection
/// leaves the outbox row pending and is retried on the next publisher pass.
/// </summary>
public sealed class DeferredRabbitMqPublisher(
    string connectionString,
    ILogger<RabbitMqEventPublisher> logger) : IEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private RabbitMqEventPublisher? _publisher;

    public async Task<bool> PublishAsync(
        string topic,
        string payload,
        IDictionary<string, object?>? headers = null,
        CancellationToken ct = default)
    {
        if (_publisher is null)
        {
            await _initializationGate.WaitAsync(ct);
            try
            {
                _publisher ??= await RabbitMqEventPublisher.CreateAsync(connectionString, logger);
            }
            finally
            {
                _initializationGate.Release();
            }
        }

        return await _publisher.PublishAsync(topic, payload, headers, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_publisher is not null) await _publisher.DisposeAsync();
        _initializationGate.Dispose();
    }
}
