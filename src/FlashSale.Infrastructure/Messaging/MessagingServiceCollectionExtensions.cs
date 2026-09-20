using FlashSale.Application.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Messaging;

/// <summary>
/// Composition helper (ADR-005): register exactly one queue provider from
/// configuration. Explicit "Messaging:Provider" wins; otherwise auto-detect
/// from connection strings (ServiceBus first, then RabbitMQ, else InMemory).
/// Unknown values fail fast.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddOrderQueue(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var configured = configuration["Messaging:Provider"];
        var rabbit = configuration.GetConnectionString("RabbitMQ");
        var serviceBus = configuration.GetConnectionString("ServiceBus");
        var provider = MessagingProviderSelector.Resolve(configured, rabbit, serviceBus);

        switch (provider)
        {
            case MessagingProvider.ServiceBus:
                services.AddSingleton<ServiceBusOrderQueue>(sp =>
                    new ServiceBusOrderQueue(serviceBus!,
                        configuration["Messaging:QueueName"] ?? "orders",
                        sp.GetRequiredService<ILogger<ServiceBusOrderQueue>>()));
                services.AddSingleton<IOrderQueueProducer>(sp => sp.GetRequiredService<ServiceBusOrderQueue>());
                services.AddSingleton<IOrderQueueConsumer>(sp => sp.GetRequiredService<ServiceBusOrderQueue>());
                break;
            case MessagingProvider.RabbitMQ:
                services.AddSingleton<RabbitMQOrderQueue>(sp =>
                    new RabbitMQOrderQueue(rabbit!,
                        configuration["Messaging:QueueName"] ?? "orders",
                        sp.GetRequiredService<ILogger<RabbitMQOrderQueue>>()));
                services.AddSingleton<IOrderQueueProducer>(sp => sp.GetRequiredService<RabbitMQOrderQueue>());
                services.AddSingleton<IOrderQueueConsumer>(sp => sp.GetRequiredService<RabbitMQOrderQueue>());
                break;
            case MessagingProvider.InMemory:
                services.AddSingleton<InMemoryOrderQueue>();
                services.AddSingleton<IOrderQueueProducer>(sp => sp.GetRequiredService<InMemoryOrderQueue>());
                services.AddSingleton<IOrderQueueConsumer>(sp => sp.GetRequiredService<InMemoryOrderQueue>());
                break;
            default:
                throw new InvalidOperationException($"Unsupported messaging provider '{provider}'.");
        }

        return services;
    }
}
