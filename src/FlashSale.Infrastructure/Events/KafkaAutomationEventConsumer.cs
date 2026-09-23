using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FlashSale.Infrastructure.Events;

/// <summary>
/// Kafka transport for automation events (V2.2). The Kafka counterpart of
/// <see cref="AutomationWorkerHost"/> — same processing, including Inbox
/// deduplication, but at-least-once offsets instead of AMQP acks.
///
/// Why it must exist before any Kafka cutover: <c>KafkaDomainEventPublisher</c>
/// had no consumer anywhere, so switching the publisher to Kafka would have sent
/// every automation event into a topic nobody read — orders would still return
/// 202 while no automation run was ever recorded.
///
/// Delivery guarantee: the offset is committed only AFTER the audit row is
/// persisted (the Inbox decision happens inside the processor). Outcomes:
///   Processed / Duplicate -> commit (making progress is safe, dedup is durable),
///   Unreadable            -> relay to &lt;topic&gt;.dlq then commit; if the relay
///                           fails the offset is NOT committed, so the message is
///                           retried instead of being silently dropped.
/// </summary>
public sealed class KafkaAutomationEventConsumer(
    string bootstrapServers,
    string topic,
    string groupId,
    AutomationEventProcessor processor,
    ILogger<KafkaAutomationEventConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            // Earliest: a fresh consumer group must not skip events that were
            // produced before it first started (e.g. an outbox backlog).
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Manual commit only — auto-commit would advance the offset before the
            // automation run is persisted and could lose an event.
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        using var dlqProducer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = bootstrapServers, Acks = Acks.All }).Build();

        consumer.Subscribe(topic);
        logger.LogInformation(
            "KafkaAutomationEventConsumer started — topic {Topic}, group {Group} (at-least-once).",
            topic, groupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromSeconds(1));
                }
                catch (ConsumeException ex)
                {
                    logger.LogError(ex, "Kafka consume failed; retrying.");
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                var eventType = ReadEventTypeHeader(result.Message.Headers);
                var outcome = await processor.ProcessAsync(
                    Encoding.UTF8.GetBytes(result.Message.Value), eventType, stoppingToken);

                switch (outcome)
                {
                    case AutomationEventOutcome.Processed:
                    case AutomationEventOutcome.Duplicate:
                        consumer.Commit(result);
                        break;

                    case AutomationEventOutcome.Unreadable:
                        if (await RelayToDlqAsync(dlqProducer, $"{topic}.dlq", result, eventType, stoppingToken))
                        {
                            consumer.Commit(result);
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
        finally
        {
            consumer.Close();
            dlqProducer.Flush(TimeSpan.FromSeconds(5));
            logger.LogInformation("KafkaAutomationEventConsumer stopped.");
        }
    }

    private static string? ReadEventTypeHeader(Headers? headers)
        => headers?.TryGetLastBytes("event-type", out var raw) == true
            ? Encoding.UTF8.GetString(raw)
            : null;

    private async Task<bool> RelayToDlqAsync(
        IProducer<string, string> producer,
        string dlqTopic,
        ConsumeResult<string, string> result,
        string? eventType,
        CancellationToken ct)
    {
        try
        {
            await producer.ProduceAsync(dlqTopic, new Message<string, string>
            {
                Key = result.Message.Key ?? string.Empty,
                Value = result.Message.Value,
                Headers = new Headers
                {
                    { "original-topic", Encoding.UTF8.GetBytes(result.Topic) },
                    { "original-partition", Encoding.UTF8.GetBytes(result.Partition.Value.ToString()) },
                    { "original-offset", Encoding.UTF8.GetBytes(result.Offset.Value.ToString()) },
                    { "event-type", Encoding.UTF8.GetBytes(eventType ?? "unknown") },
                    { "dlq-reason", Encoding.UTF8.GetBytes("unreadable automation event") }
                }
            }, ct);

            logger.LogWarning(
                "Relayed unreadable event (partition {Partition}, offset {Offset}, type={EventType}) to {Dlq}.",
                result.Partition.Value, result.Offset.Value, eventType ?? "unknown", dlqTopic);
            return true;
        }
        catch (Exception ex)
        {
            // Deliberately no commit: retrying the same message hurts throughput
            // but beats dropping an event nobody can see.
            logger.LogError(ex,
                "Failed to relay unreadable event (offset {Offset}) to {Dlq}; offset left uncommitted for retry.",
                result.Offset.Value, dlqTopic);
            return false;
        }
    }
}