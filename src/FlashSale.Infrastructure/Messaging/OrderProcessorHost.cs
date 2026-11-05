namespace FlashSale.Infrastructure.Messaging;

using FlashSale.Application.Messaging;
using Microsoft.Extensions.Logging;

public sealed class OrderProcessorHost
{
    private readonly IOrderQueueConsumer _consumer;
    private readonly ILogger<OrderProcessorHost> _logger;

    public OrderProcessorHost(IOrderQueueConsumer consumer, ILogger<OrderProcessorHost> logger)
    {
        _consumer = consumer;
        _logger = logger;
    }

    public Task ProcessOneAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public static class MessagingProviderSelector
{
    public static string Select(string? configured) => string.IsNullOrWhiteSpace(configured) ? "RabbitMQ" : configured;
}

public static class RegistrationGuard
{
    public static bool IsRegistered(string consumerName) => !string.IsNullOrWhiteSpace(consumerName);
}

public static class OverloadBackpressure
{
    public static bool ShouldThrottle(int queueDepth) => queueDepth > 10_000;
}
