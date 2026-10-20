using System.Diagnostics.Metrics;

namespace FlashSale.Infrastructure.Messaging;

public static class WorkerTelemetry
{
    public const string MeterName = "FlashSale.Worker";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static long _outboxStuckMessages;

    public static readonly Counter<long> QueueMessagesProcessed = Meter.CreateCounter<long>(
        "flashsale.worker.messages.processed",
        unit: "{message}",
        description: "Queue messages processed or dead-lettered by the worker.");

    public static readonly Counter<long> QueueMessageRetries = Meter.CreateCounter<long>(
        "flashsale.worker.messages.retried",
        unit: "{message}",
        description: "Queue messages re-enqueued after a transient processing failure.");

    public static readonly Counter<long> OutboxMessagesDispatched = Meter.CreateCounter<long>(
        "flashsale.worker.outbox.dispatched",
        unit: "{event}",
        description: "Outbox messages successfully dispatched.");

    public static readonly ObservableGauge<long> OutboxStuckMessages = Meter.CreateObservableGauge(
        "flashsale.worker.outbox.stuck",
        () => Interlocked.Read(ref _outboxStuckMessages),
        unit: "{event}",
        description: "Outbox messages dead-lettered or awaiting retry.");

    public static void SetOutboxStuckMessages(long count) =>
        Interlocked.Exchange(ref _outboxStuckMessages, count);
}