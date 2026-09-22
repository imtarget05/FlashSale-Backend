using FlashSale.Application.Messaging;
using FlashSale.Infrastructure.Messaging;
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

    /// <summary>
    /// Explicitly named InMemory needs no connection string; that is the whole
    /// point of it.
    /// </summary>
    [Fact]
    public void Explicit_InMemory_Needs_No_ConnectionString() =>
        Assert.Equal(MessagingProvider.InMemory,
            MessagingProviderSelector.Resolve("InMemory", null, null));

    [Fact]
    public void AutoDetect_Prefers_ServiceBus() =>
        Assert.Equal(MessagingProvider.ServiceBus,
            MessagingProviderSelector.Resolve(null, "amqp://x", "sb-conn"));

    [Fact]
    public void AutoDetect_FallsBack_To_RabbitMQ() =>
        Assert.Equal(MessagingProvider.RabbitMQ,
            MessagingProviderSelector.Resolve("", "amqp://x", null));

    /// <summary>
    /// THE Phase 7C fix. Nothing configured at all must not resolve to InMemory:
    /// two pods each with their own in-process broker look healthy and transfer
    /// nothing.
    /// </summary>
    [Fact]
    public void Nothing_Configured_FailsFast_Instead_Of_Silently_InMemory()
    {
        var ex = Assert.Throws<MessagingConfigurationException>(() =>
            MessagingProviderSelector.Resolve(null, null, null));

        Assert.Contains("InMemory", ex.Message, StringComparison.Ordinal);
        Assert.Contains("per-process", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("   ", null, null)]
    [InlineData(null, "", null)]
    [InlineData(null, null, "  ")]
    public void Blank_Configuration_Is_Treated_As_Absent(string? provider, string? rabbit, string? sb) =>
        Assert.Throws<MessagingConfigurationException>(() =>
            MessagingProviderSelector.Resolve(provider, rabbit, sb));

    /// <summary>
    /// Naming a provider without its connection string is a broken deployment,
    /// not a fallback opportunity.
    /// </summary>
    [Fact]
    public void Explicit_RabbitMQ_Without_ConnectionString_FailsFast()
    {
        var ex = Assert.Throws<MessagingConfigurationException>(() =>
            MessagingProviderSelector.Resolve("RabbitMQ", null, null));

        Assert.Contains("ConnectionStrings:RabbitMQ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_ServiceBus_Without_ConnectionString_FailsFast()
    {
        var ex = Assert.Throws<MessagingConfigurationException>(() =>
            MessagingProviderSelector.Resolve("ServiceBus", "amqp://x", null));

        Assert.Contains("ConnectionStrings:ServiceBus", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_Provider_FailsFast_With_The_Offending_Value()
    {
        var ex = Assert.Throws<MessagingConfigurationException>(() =>
            MessagingProviderSelector.Resolve("Kafka", null, null));

        Assert.Contains("'Kafka'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Valid values", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard is documented as case-insensitive on the provider name; pin it
    /// so a refactor cannot quietly make "RABBITMQ" a startup failure.
    /// </summary>
    [Theory]
    [InlineData("rabbitmq")]
    [InlineData("RABBITMQ")]
    public void Provider_Name_Is_Case_Insensitive(string value) =>
        Assert.Equal(MessagingProvider.RabbitMQ,
            MessagingProviderSelector.Resolve(value, "amqp://x", null));

    [Fact]
    public void Provider_Name_Tolerates_Whitespace() =>
        Assert.Equal(MessagingProvider.RabbitMQ,
            MessagingProviderSelector.Resolve("  RabbitMQ  ", "amqp://x", null));
}
