using FlashSale.Application.Events;
using FlashSale.Domain.Automation;
using FlashSale.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Events;

/// <summary>
/// Composition helpers (spec §1/§11): exactly one event publisher — RabbitMQ
/// when Messaging:Provider=RabbitMQ, in-memory otherwise. Follows the same
/// explicit-provider rule as the order queue (ADR-005): the automation worker
/// only starts for RabbitMQ, so API and worker resolve the same topology.
/// </summary>
public static class DomainEventServiceCollectionExtensions
{
    public static IServiceCollection AddDomainEventPublisher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rabbit = configuration.GetConnectionString("RabbitMQ");
        var provider = configuration["Messaging:Provider"];

        if (string.Equals(provider, "RabbitMQ", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(rabbit))
        {
            services.AddSingleton<IDomainEventPublisher>(sp =>
                RabbitMqDomainEventPublisher.CreateAsync(
                        rabbit!,
                        sp.GetRequiredService<ILogger<RabbitMqDomainEventPublisher>>())
                    .GetAwaiter().GetResult());
        }
        else
        {
            var inMemory = new InMemoryDomainEventPublisher();
            services.AddSingleton<IDomainEventPublisher>(inMemory);
            services.AddSingleton<IAutomationEventPublisher>(inMemory);
            services.AddSingleton(inMemory);
        }

        return services;
    }

    public static IServiceCollection AddAutomationWorkerHost(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Audit repository is provider-independent (DB-backed): the payment
        // timeout scan runs on ANY messaging provider, so this registration
        // must stay OUTSIDE the RabbitMQ gate below.
        services.AddScoped<IAutomationRunRepository, AutomationRunRepository>();

        var rabbit = configuration.GetConnectionString("RabbitMQ");
        var provider = configuration["Messaging:Provider"];

        if (string.Equals(provider, "RabbitMQ", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(rabbit))
        {
            services.AddHostedService(sp =>
                AutomationWorkerHost.CreateAsync(
                        rabbit!,
                        sp.GetRequiredService<IServiceScopeFactory>(),
                        sp.GetRequiredService<ILogger<AutomationWorkerHost>>())
                    .GetAwaiter().GetResult());
        }

        return services;
    }
}