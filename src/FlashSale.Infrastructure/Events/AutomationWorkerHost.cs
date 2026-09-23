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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutomationWorkerHost> _logger;
    private readonly System.Threading.Channels.Channel<Delivery> _deliveryChannel;
    private readonly AsyncEventingBasicConsumer _consumer;
    private readonly string _queueName;

    private AutomationWorkerHost(IConnection connection, IChannel channel,
        IServiceScopeFactory scopeFactory, ILogger<AutomationWorkerHost> logger, string queueName)
    {
        _connection = connection;
        _channel = channel;
        _scopeFactory = scopeFactory;
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
        IServiceScopeFactory scopeFactory,
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
        var host = new AutomationWorkerHost(connection, channel, scopeFactory, logger, queueName);
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
        var json = Encoding.UTF8.GetString(delivery.Body);
        // AMQP longstr headers decode as byte[] in RabbitMQ.Client, and
        // byte[].ToString() is "System.Byte[]" (non-null!) — so the typed
        // reader MUST run first, otherwise every event lands in the
        // _ => null arm below and is dead-lettered as unreadable.
        string? eventType = ReadStringHeader(delivery, "event-type");
        if (eventType is null)
        {
            try
            {
                eventType = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("EventType").GetString();
            }
            catch (JsonException) { /* fall through to dead-letter */ }
        }

        var evt = DeserializeSafe(json, eventType);
        if (evt is null)
        {
            // Poison message: malformed body (e.g. trailing bytes after a valid
            // JSON document). The old direct Deserialize let JsonException
            // escape ExecuteAsync; with the default StopHost behavior one bad
            // message shut the whole worker down, and the unacked delivery was
            // requeued — so the container crash-looped on the SAME message
            // forever (observed: exit 0 "Completed", restartCount climbed to 4+).
            // Dead-letter it instead, exactly like an unknown event-type.
            _logger.LogWarning(
                "Unreadable automation event (type={EventType}) — dead-lettering.",
                eventType);
            await _channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<IAutomationRunRepository>();

        var run = new AutomationRun
        {
            WorkflowName = WorkflowFor(evt),
            TriggerType = "domain.event",
            TriggerId = evt.EventId,
            Status = AutomationRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CorrelationId = evt.CorrelationId
        };
        await runs.CreateAsync(run, ct);

        try
        {
            await ProcessAsync(evt, run, ct);
            run.Status = AutomationRunStatus.Success;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ResultSummary = $"Processed {evt.EventType}.";
        }
        catch (Exception ex)
        {
            run.Status = AutomationRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ErrorCode = ex.GetType().Name;
            run.ErrorMessage = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            _logger.LogError(ex, "Automation run {RunId} failed for {EventType}.", run.Id, evt.EventType);
        }

        await runs.UpdateAsync(run, ct);
        await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
    }

    private static string? ReadStringHeader(Delivery delivery, string key)
    {
        if (delivery.Headers is not { } headers || !headers.TryGetValue(key, out var raw))
            return null;
        return raw switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => raw?.ToString()
        };
    }

    /// <summary>
    /// Deserialize without ever letting a System.Text.Json parse error escape
    /// into the BackgroundService — a malformed payload must dead-letter, not
    /// stop the host (see HandleAsync).
    /// </summary>
    private static DomainEvent? DeserializeSafe(string json, string? eventType)
    {
        try
        {
            return Deserialize(json, eventType);
        }
        catch (JsonException)
        {
            // Intentionally no throw: caller logs eventType and rejects the delivery.
            return null;
        }
    }

    private static DomainEvent? Deserialize(string json, string? eventType) => eventType switch
    {
        "order.created" => JsonSerializer.Deserialize<OrderCreatedEvent>(json),
        "inventory.reserved" => JsonSerializer.Deserialize<InventoryReservedEvent>(json),
        "payment.completed" => JsonSerializer.Deserialize<PaymentCompletedEvent>(json),
        "payment.failed" => JsonSerializer.Deserialize<PaymentFailedEvent>(json),
        "payment.expired" => JsonSerializer.Deserialize<PaymentExpiredEvent>(json),
        "order.confirmed" => JsonSerializer.Deserialize<OrderConfirmedEvent>(json),
        "order.cancelled" => JsonSerializer.Deserialize<OrderCancelledEvent>(json),
        "inventory.released" => JsonSerializer.Deserialize<InventoryReleasedEvent>(json),
        "inventory.low_stock" => JsonSerializer.Deserialize<InventoryLowStockEvent>(json),
        _ => null
    };

    private static string WorkflowFor(DomainEvent evt) => evt switch
    {
        OrderCreatedEvent => Domain.Automation.AutomationWorkflow.OrderProcessing.ToString(),
        PaymentCompletedEvent or PaymentFailedEvent or PaymentExpiredEvent
            => Domain.Automation.AutomationWorkflow.PaymentTimeout.ToString(),
        OrderCancelledEvent or InventoryReleasedEvent or InventoryReservedEvent or InventoryLowStockEvent
            => Domain.Automation.AutomationWorkflow.InventoryAutomation.ToString(),
        OrderConfirmedEvent => Domain.Automation.AutomationWorkflow.OrderProcessing.ToString(),
        _ => "unknown"
    };

    private Task ProcessAsync(DomainEvent evt, AutomationRun run, CancellationToken ct)
    {
        // Workflow-specific handlers land in later phases; audit-first means every
        // event type is already persisted + logged even before side effects exist.
        _logger.LogInformation(
            "Automation {Workflow}: handled {EventType} {EventId} (correlation {CorrelationId}).",
            run.WorkflowName, evt.EventType, evt.EventId, evt.CorrelationId);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await _channel.CloseAsync(cancellationToken: cancellationToken); } catch { }
        await base.StopAsync(cancellationToken);
        try { await _channel.DisposeAsync(); } catch { }
        try { await _connection.DisposeAsync(); } catch { }
    }
}
