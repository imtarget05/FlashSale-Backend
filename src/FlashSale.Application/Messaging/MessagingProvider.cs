namespace FlashSale.Application.Messaging;

/// <summary>
/// Messaging provider strategy (ADR-005): local development uses RabbitMQ,
/// Azure production uses managed Service Bus, tests use InMemory.
/// Application code depends only on IOrderQueueProducer/IOrderQueueConsumer
/// — never on a broker SDK.
/// </summary>
public enum MessagingProvider
{
    InMemory,
    RabbitMQ,
    ServiceBus
}

public static class MessagingProviderSelector
{
    /// <summary>
    /// Resolve exactly one provider. Explicit configured value wins;
    /// empty/whitespace falls back to connection-string auto-detection.
    /// Unknown values fail fast.
    /// </summary>
    public static MessagingProvider Resolve(
        string? configured,
        string? rabbitMqConnectionString,
        string? serviceBusConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Enum.TryParse<MessagingProvider>(configured.Trim(), ignoreCase: true, out var parsed))
                return parsed;
            throw new InvalidOperationException(
                $"Unknown messaging provider '{configured}'. Valid values: InMemory, RabbitMQ, ServiceBus.");
        }

        if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
            return MessagingProvider.ServiceBus;
        if (!string.IsNullOrWhiteSpace(rabbitMqConnectionString))
            return MessagingProvider.RabbitMQ;
        return MessagingProvider.InMemory;
    }
}
