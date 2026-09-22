using FlashSale.Application.Messaging;
using FlashSale.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlashSale.UnitTests;

/// <summary>
/// Guards the composition-root half of the Phase 7C fix: the same rules have to
/// hold once a real host (API/Worker) asks for the queue, including the
/// Production-environment ban on a per-process broker.
/// </summary>
public class MessagingRegistrationGuardTests
{
    private static IConfiguration Config(
        string? provider, string? rabbit, string? serviceBus, string? queueName = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = provider,
            ["Messaging:QueueName"] = queueName,
            ["ConnectionStrings:RabbitMQ"] = rabbit,
            ["ConnectionStrings:ServiceBus"] = serviceBus
        };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceProvider Build(
        string? provider, string? rabbit, string? serviceBus, string? environmentName) =>
        new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddOrderQueue(Config(provider, rabbit, serviceBus), environmentName)
            .BuildServiceProvider();

    private static ServiceCollection Registered(
        string? provider, string? rabbit, string? serviceBus, string? environmentName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddOrderQueue(Config(provider, rabbit, serviceBus), environmentName);
        return services;
    }

    /// <summary>
    /// Local/test path: explicitly asking for InMemory must work, and both ports
    /// must share ONE instance (otherwise the consumer never sees the produced
    /// message even inside a single process).
    /// </summary>
    [Fact]
    public void Explicit_InMemory_Registers_One_Shared_Instance()
    {
        using var provider = Build("InMemory", null, null, environmentName: "Development");

        var producer = provider.GetRequiredService<IOrderQueueProducer>();
        var consumer = provider.GetRequiredService<IOrderQueueConsumer>();

        Assert.IsType<InMemoryOrderQueue>(producer);
        Assert.Same(producer, consumer);
    }

    /// <summary>
    /// AKS path: RabbitMQ is selected by name. Asserting on the descriptor
    /// instead of resolving keeps this test offline — the adapter opens a socket
    /// in its constructor, so a real resolution would need a broker.
    /// </summary>
    [Fact]
    public void Explicit_RabbitMQ_Registers_The_RabbitMQ_Adapter()
    {
        var services = Registered("RabbitMQ", "amqp://x", null, "Production");

        Assert.Contains(services, d => d.ServiceType == typeof(RabbitMQOrderQueue));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(InMemoryOrderQueue));
    }

    /// <summary>
    /// What docker-compose and the AKS overlay do today: connection string only,
    /// no explicit Messaging:Provider. Auto-detection must still reach RabbitMQ.
    /// </summary>
    [Fact]
    public void AutoDetected_RabbitMQ_Registers_The_RabbitMQ_Adapter()
    {
        var services = Registered(null, "amqp://x", null, "Development");

        Assert.Contains(services, d => d.ServiceType == typeof(RabbitMQOrderQueue));
    }

    /// <summary>
    /// The production trap this pass exists to close: ASP.NET defaults to
    /// Production when ASPNETCORE_ENVIRONMENT is unset, which is how both AKS
    /// containers run. An explicit InMemory there must abort startup rather than
    /// roll out two pods that never talk to each other.
    /// </summary>
    [Fact]
    public void InMemory_Is_Rejected_In_Production()
    {
        var ex = Assert.Throws<MessagingConfigurationException>(() =>
            Build("InMemory", null, null, environmentName: "Production"));

        Assert.Contains("Production", ex.Message, StringComparison.Ordinal);
        Assert.Contains("per-process", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Environment names are matched case-insensitively and "production" is a
    /// legal Helm value typo, so the guard must not be bypassed by casing or
    /// surrounding whitespace.
    /// </summary>
    [Theory]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    [InlineData(" Production ")]
    public void Production_Guard_Is_Case_And_Whitespace_Insensitive(string environmentName) =>
        Assert.Throws<MessagingConfigurationException>(() =>
            Build("InMemory", null, null, environmentName));

    /// <summary>
    /// A misconfigured broker must fail at registration time, not on the first
    /// order — and must not degrade into InMemory.
    /// </summary>
    [Fact]
    public void Missing_Broker_Configuration_Fails_At_Registration() =>
        Assert.Throws<MessagingConfigurationException>(() =>
            Build(null, null, null, environmentName: "Production"));

    /// <summary>
    /// Null environment (unit tests, direct composition) keeps the selector
    /// honest but adds no environment policy on top.
    /// </summary>
    [Fact]
    public void Null_Environment_Allows_Explicit_InMemory()
    {
        using var provider = Build("InMemory", null, null, environmentName: null);
        Assert.IsType<InMemoryOrderQueue>(provider.GetRequiredService<IOrderQueueProducer>());
    }
}
