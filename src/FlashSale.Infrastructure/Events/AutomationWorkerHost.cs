using FlashSale.Domain.Automation;
using FlashSale.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace FlashSale.Infrastructure.Events;

/// <summary>
/// Background host for automation event processing (spec §1, §11): consumes
/// the automation.events topic queue, wraps each event in an AutomationRun
/// audit record (Running → Success/Failed) and delegates workflow logic.
/// At-least-once semantics: ack only after the run record is persisted.
/// </summary>
public sealed class AutomationWorkerHost : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly AutomationEventProcessor _processor;
    private readonly ILogger<AutomationWorkerHost> _logger;
    private readonly System.Threading.Channels.Channel<Delivery> _deliveryChannel;
    private readonly AsyncEventingBasicConsumer _consumer;
    private readonly string _queueName;

    private AutomationWorkerHost(IConnection connection, IChannel channel,
        AutomationEventProcessor processor, ILogger<AutomationWorkerHost> logger, string queueName)
    {
        _connection = connection;
        _channel = channel;
        _processor = processor;
        _logger = logger;
        _queueName = queueName;
        _deliveryChannel = System.Threading.Channels.Channel.CreateBounded<Delivery>(1000);
        _consumer = new AsyncEventingBasicConsumer(channel);
        _consumer.ReceivedAsync += async (_, ea) =>
        {
            // RabbitMQ.Client v7 OWNS the memory behind ea.Body and documents it
            // as valid ONLY inside this handler (v7-MIGRATION.md: "the
            // ReadOnlyMemory<byte> that represents the message body is owned by
            // this library, and that memory is only valid for application use
            // within the context of the executing ReceivedAsync event ... you
            // MUST copy the data"). This host defers handling through an
            // in-process channel, so the pooled buffer is recycled before
            // HandleAsync reads it — the observed symptom was a truncated event
            // document that dead-lettered as "Unreadable automation event".
            // Copy the body AND the header values here, inside the handler.
            await _deliveryChannel.Writer.WriteAsync(
                new Delivery(ea.DeliveryTag, ea.Body.ToArray(),
                    SnapshotHeaders(ea.BasicProperties.Headers)));
        };
    }

    /// <summary>
    /// One delivery with OWNED copies of everything HandleAsync reads later:
    /// the body bytes and each header value (AMQP longstr headers arrive as
    /// <c>byte[]</c>). Nothing here may alias library-owned memory.
    /// </summary>
    private sealed record Delivery(
        ulong DeliveryTag, byte[] Body, Dictionary<string, object?>? Headers);

    private static Dictionary<string, object?>? SnapshotHeaders(
        IDictionary<string, object?>? headers)
    {
        if (headers is null) return null;
        var copy = new Dictionary<string, object?>(headers.Count, StringComparer.Ordinal);
        foreach (var pair in headers)
        {
            copy[pair.Key] = pair.Value is byte[] bytes ? bytes.ToArray() : pair.Value;
        }
        return copy;
    }

    public static async Task<AutomationWorkerHost> CreateAsync(
        string connectionString,
        AutomationEventProcessor processor,
        ILogger<AutomationWorkerHost> logger,
        string exchangeName = "automation.events",
        string queueName = "automation.events")
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Topic, durable: true);
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false,
            autoDelete: false, arguments: null);
        await channel.QueueBindAsync(queueName, exchangeName, "#");
        var host = new AutomationWorkerHost(connection, channel, processor, logger, queueName);
        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: host._consumer);
        return host;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AutomationWorkerHost started — consuming {Queue} (at-least-once, audited runs).",
            _queueName);

        while (!stoppingToken.IsCancellationRequested)
        {
            Delivery delivery;
            try
            {
                using var poll = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                poll.CancelAfter(TimeSpan.FromSeconds(2));
                delivery = await _deliveryChannel.Reader.ReadAsync(poll.Token);
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested) break;
                continue; // poll window elapsed with no message — keep looping
            }

            await HandleAsync(delivery, stoppingToken);
        }

        _logger.LogInformation("AutomationWorkerHost stopping.");
    }

    private async Task HandleAsync(Delivery delivery, CancellationToken ct)
    {
        // AMQP longstr headers decode as byte[] in RabbitMQ.Client, and
        // byte[].ToString() is "System.Byte[]" (non-null!) — the typed reader
        // must run first, otherwise every event lands in the unknown-type arm
        // and is dead-lettered as unreadable.
        var eventType = AutomationEventProcessor.ReadStringHeader(
            delivery.Headers is { } headers && headers.TryGetValue("event-type", out var raw) ? raw : null);

        var outcome = await _processor.ProcessAsync(delivery.Body, eventType, ct);

        if (outcome == AutomationEventOutcome.Unreadable)
        {
            // Dead-letter rather than requeue: the original code let the parse
            // exception escape ExecuteAsync, so with the default StopHost behavior
            // one bad message stopped the worker and the unacked delivery was
            // requeued — the container crash-looped on the SAME message forever.
            await _channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false);
            return;
        }

        // Ack only after the run record is persisted (at-least-once).
        await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await _channel.CloseAsync(cancellationToken: cancellationToken); } catch { }
        await base.StopAsync(cancellationToken);
        try { await _channel.DisposeAsync(); } catch { }
        try { await _connection.DisposeAsync(); } catch { }
    }
}
