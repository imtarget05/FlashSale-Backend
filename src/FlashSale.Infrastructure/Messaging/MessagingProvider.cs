using System;

namespace FlashSale.Infrastructure.Messaging;

/// <summary>
/// Messaging provider strategy (ADR-005): local development uses RabbitMQ,
/// Azure production uses managed Service Bus, tests use InMemory.
/// Infrastructure concern (moved out of Application): the enum names concrete
/// brokers and the selector reads connection-string config paths — neither
/// belongs in the Application layer. Application code still depends only on
/// IOrderQueueProducer/IOrderQueueConsumer — never on a broker SDK.
/// </summary>
public enum MessagingProvider
{
    InMemory,
    RabbitMQ,
    ServiceBus
}

/// <summary>
/// Raised when messaging configuration cannot produce a single, correct
/// provider. It is deliberately a distinct type so a host can tell
/// "misconfigured deployment" apart from an arbitrary startup exception, and
/// so the failure is not swallowed by a generic catch.
/// </summary>
public sealed class MessagingConfigurationException : InvalidOperationException
{
    public MessagingConfigurationException(string message) : base(message) { }
}

public static class MessagingProviderSelector
{
    /// <summary>
    /// Resolve exactly one provider. Explicit configured value wins;
    /// empty/whitespace falls back to connection-string auto-detection.
    /// Unknown values fail fast.
    /// </summary>
    /// <remarks>
    /// Phase 7C correctness fix: auto-detection may resolve RabbitMQ or Service
    /// Bus (both are real, shared brokers that docker-compose and the AKS overlay
    /// configure through connection strings alone), but it may NEVER resolve to
    /// InMemory. <see cref="MessagingProvider.InMemory"/> is a per-process broker,
    /// so an API pod and a worker pod that each silently select it are not
    /// connected to each other: the API enqueues into its own heap, the worker
    /// waits on a queue that has no producer, every pod reports Healthy, and the
    /// "order reaches Completed" gate is never satisfied — with no error anywhere
    /// to explain why. InMemory therefore has to be chosen EXPLICITLY, which is
    /// also the only way a test or a local run can ask for it.
    /// </remarks>
    public static MessagingProvider Resolve(
        string? configured,
        string? rabbitMqConnectionString,
        string? serviceBusConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Enum.TryParse<MessagingProvider>(configured.Trim(), ignoreCase: true, out var parsed))
                throw new MessagingConfigurationException(
                    $"Unknown messaging provider '{configured}'. Valid values: InMemory, RabbitMQ, ServiceBus.");

            // Naming a provider is not enough: the adapter for it needs its
            // connection string. Without this check the failure surfaced later
            // as a NullReferenceException inside the broker client.
            var missing = parsed switch
            {
                MessagingProvider.RabbitMQ when string.IsNullOrWhiteSpace(rabbitMqConnectionString)
                    => "ConnectionStrings:RabbitMQ",
                MessagingProvider.ServiceBus when string.IsNullOrWhiteSpace(serviceBusConnectionString)
                    => "ConnectionStrings:ServiceBus",
                _ => null
            };
            if (missing is not null)
                throw new MessagingConfigurationException(
                    $"Messaging:Provider='{parsed}' requires '{missing}' to be configured, but it is empty. " +
                    "Refusing to start: a named provider with no connection string cannot reach any broker.");

            return parsed;
        }

        if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
            return MessagingProvider.ServiceBus;
        if (!string.IsNullOrWhiteSpace(rabbitMqConnectionString))
            return MessagingProvider.RabbitMQ;

        throw new MessagingConfigurationException(
            "No messaging provider could be resolved: Messaging:Provider is unset and neither " +
            "ConnectionStrings:RabbitMQ nor ConnectionStrings:ServiceBus is configured. " +
            "Set Messaging:Provider=RabbitMQ (or ServiceBus) with its connection string, or " +
            "Messaging:Provider=InMemory explicitly for a single-process local/test run. " +
            "InMemory is not auto-detected because it is per-process and would silently split the " +
            "API and the worker into two disconnected brokers.");
    }
}