using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Messaging;

/// <summary>
/// Background host for the async path: dequeue -> process -> ack, with
/// exponential-backoff retries and a dead-letter path. Used in-process by the
/// API (dev queue) and by Order.Worker (Service Bus).
/// </summary>
public sealed class OrderProcessorHost(
    IOrderQueueConsumer consumer,
    IOrderQueueProducer producer,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderProcessorHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "OrderProcessorHost started — at-least-once delivery, idempotent consumer, max {Max} attempts.",
            OrderProcessor.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            QueueEntry? entry = null;
            try
            {
                entry = await consumer.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (entry is null)
            {
                await Task.Delay(100, stoppingToken);
                continue;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<OrderProcessor>();

            try
            {
                await processor.ProcessAsync(entry.Message, stoppingToken);
                if (entry.Complete is not null) await entry.Complete();
            }
            catch (Domain.StockDriftException)
            {
                // Never retry drift: the reservation was wrong, not the transport.
                logger.LogCritical(
                    "STOCK-DRIFT for {Key} (product {Pid}) — dead-lettering. Runbook: resolve drift, then POST /internal/resync-stock/{{id}}.",
                    entry.Message.IdempotencyKey, entry.Message.ProductId);
                await DeadLetterAsync(entry);
            }
            catch (Exception ex) when (entry.Message.Attempt + 1 < OrderProcessor.MaxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, entry.Message.Attempt));
                logger.LogWarning(ex,
                    "Transient failure for {Key} (attempt {Attempt}/{Max}) — retrying in {Delay}.",
                    entry.Message.IdempotencyKey, entry.Message.Attempt + 1,
                    OrderProcessor.MaxAttempts, delay);
                await Task.Delay(delay, stoppingToken);
                await producer.EnqueueAsync(entry.Message with { Attempt = entry.Message.Attempt + 1 }, stoppingToken);
                if (entry.Complete is not null) await entry.Complete(); // original handled; copy re-queued
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex,
                    "Message {Key} exhausted {Attempt} attempts — moving to DLQ.",
                    entry.Message.IdempotencyKey, entry.Message.Attempt + 1);
                await DeadLetterAsync(entry);
            }
        }
    }

    private async Task DeadLetterAsync(QueueEntry entry)
    {
        // Structured stdout log instead of a local logs/dlq.log file: on
        // Kubernetes a pod restart wipes the container filesystem and the record
        // with it. Container platforms ship stdout to the central log store, so
        // this line survives restarts and stays queryable.
        try
        {
            logger.LogCritical(
                "DLQ|{Timestamp}|{IdempotencyKey}|product={ProductId}|qty={Quantity}|attempt={Attempt}",
                DateTimeOffset.UtcNow, entry.Message.IdempotencyKey,
                entry.Message.ProductId, entry.Message.Quantity, entry.Message.Attempt + 1);
        }
        catch
        {
            // Logging must never crash the processor.
        }

        if (entry.DeadLetter is not null)
            await entry.DeadLetter();
        else if (entry.Complete is not null)
            await entry.Complete();
    }
}
