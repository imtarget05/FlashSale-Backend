using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpsBridge.Application.Messaging;
using OpsBridge.Infrastructure;

namespace OpsBridge.UnitTests;

public class MessagingRegistrationTests
{
    [Fact]
    public void EnabledMessaging_RegistersPublisherAndBothBackgroundServices()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpsBridge:Messaging:Enabled"] = "true",
            ["ConnectionStrings:RabbitMQ"] = "amqp://guest:guest@localhost:5672/",
        }).Build();
        var services = new ServiceCollection();

        services.AddOpsBridgeInfrastructure(config);

        Assert.Contains(services, d => d.ServiceType == typeof(IEventPublisher));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IHostedService)));
    }

    [Fact]
    public void EnabledMessaging_WithoutBrokerConnection_FailsAtComposition()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpsBridge:Messaging:Enabled"] = "true",
        }).Build();

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddOpsBridgeInfrastructure(config));
    }
}
