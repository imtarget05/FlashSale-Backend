using FlashSale.Application.Messaging;
using Xunit;

namespace FlashSale.UnitTests;

public class MessagingProviderSelectorTests
{
    [Fact]
    public void Explicit_RabbitMQ_Wins() =>
        Assert.Equal(MessagingProvider.RabbitMQ,
            MessagingProviderSelector.Resolve("RabbitMQ", "amqp://x", "sb-conn"));

    [Fact]
    public void Explicit_ServiceBus_Wins() =>
        Assert.Equal(MessagingProvider.ServiceBus,
            MessagingProviderSelector.Resolve("ServiceBus", "amqp://x", "sb-conn"));

    [Fact]
    public void Explicit_InMemory_Wins() =>
        Assert.Equal(MessagingProvider.InMemory,
            MessagingProviderSelector.Resolve("InMemory", "amqp://x", "sb-conn"));

    [Fact]
    public void AutoDetect_Prefers_ServiceBus() =>
        Assert.Equal(MessagingProvider.ServiceBus,
            MessagingProviderSelector.Resolve(null, "amqp://x", "sb-conn"));

    [Fact]
    public void AutoDetect_FallsBack_To_RabbitMQ() =>
        Assert.Equal(MessagingProvider.RabbitMQ,
            MessagingProviderSelector.Resolve("", "amqp://x", null));

    [Fact]
    public void AutoDetect_FallsBack_To_InMemory() =>
        Assert.Equal(MessagingProvider.InMemory,
            MessagingProviderSelector.Resolve(null, null, null));

    [Fact]
    public void Unknown_Provider_FailsFast() =>
        Assert.Throws<InvalidOperationException>(() =>
            MessagingProviderSelector.Resolve("Kafka", null, null));
}
