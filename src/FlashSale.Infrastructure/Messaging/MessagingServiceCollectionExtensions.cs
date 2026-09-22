using FlashSale.Application.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Messaging;

/// <summary>
/// Composition helper (ADR-005): register exactly one queue provider from
/// configuration. Explicit "Messaging:Provider" wins; otherwise auto-detect
/// from connection strings (ServiceBus first, then RabbitMQ).
/// Unknown values, a named provider with no connection string, and an absent
/// broker configuration all fail fast — InMemory is never auto-detected.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>The only environment name that forbids a per-process broker.</summary>
    public const string ProductionEnvironmentName = "Production";

    /// <summary>
    /// Register the queue provider.
    /// </summary>
    /// <param name="environmentName">
    /// The host environment name (WebApplicationBuilder/HostApplicationBuilder
    /// <c>Environment.EnvironmentName</c>). When it is <c>Production</c> — the
    /// ASP.NET default when no <c>ASPNETCORE_ENVIRONMENT</c> is set, which is what
    /// both AKS containers run as — <c>InMemory</c> is rejected outright. A
    /// per-process broker cannot satisfy a multi-pod deployment, and the resulting
    /// failure mode is silent: pods go Ready while orders never complete.
    /// Null/empty (unit tests, direct composition) leaves the choice to the caller.
    /// </param>
    public static IServiceCollection AddOrderQueue(
        this IServiceCollection services,
        IConfiguration configuration,
        string? environmentName = null)
    {
        var configured = configuration["Messaging:Provider"];
        var rabbit = configuration.GetConnectionString("RabbitMQ");
        var serviceBus = configuration.GetConnectionString("ServiceBus");
        var provider = MessagingProviderSelector.Resolve(configured, rabbit, serviceBus);

        if (provider == MessagingProvider.InMemory
            && string.Equals(environmentName?.Trim(), ProductionEnvironmentName, StringComparison.OrdinalIgnoreCase))
        {
            throw new MessagingConfigurationException(
                "Messaging:Provider='InMemory' is not allowed in the Production environment: the queue is " +
                "per-process, so the API and the worker would run two disconnected brokers and no order " +
                "would ever reach Completed. Configure ConnectionStrings:RabbitMQ (or ServiceBus) and set " +
                "Messaging:Provider accordingly.");
        }

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
                    RabbitMQOrderQueue.CreateAsync(rabbit!,
                        configuration["Messaging:QueueName"] ?? "orders",
                        sp.GetRequiredService<ILogger<RabbitMQOrderQueue>>())
                        .GetAwaiter().GetResult());
                services.AddSingleton<IOrderQueueProducer>(sp => sp.GetRequiredService<RabbitMQOrderQueue>());
                services.AddSingleton<IOrderQueueConsumer>(sp => sp.GetRequiredService<RabbitMQOrderQueue>());
                break;
            case MessagingProvider.InMemory:
                services.AddSingleton<InMemoryOrderQueue>();
                services.AddSingleton<IOrderQueueProducer>(sp => sp.GetRequiredService<InMemoryOrderQueue>());
                services.AddSingleton<IOrderQueueConsumer>(sp => sp.GetRequiredService<InMemoryOrderQueue>());
                break;
            default:
                throw new MessagingConfigurationException($"Unsupported messaging provider '{provider}'.");
        }

        return services;
    }
}
