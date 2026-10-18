using RabbitMQ.Client;

namespace OpsBridge.Infrastructure.Messaging;

public static class RabbitMqTopology
{
    public const string Exchange = "opsbridge.events.topic";
    public const string InboundQueue = "opsbridge.goods-receipts.v1";
    public const string InboundRoutingKey = "opsbridge.events.inbound.#";

    public const string DlxExchange = "opsbridge.events.dlq.exchange";
    public const string DlqQueue = "opsbridge.events.dlq";

    public static async Task InitializeTopologyAsync(IChannel channel)
    {
        // 1. Declare Dead-Letter Exchange and Queue
        await channel.ExchangeDeclareAsync(DlxExchange, ExchangeType.Topic, durable: true);
        await channel.QueueDeclareAsync(
            DlqQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        await channel.QueueBindAsync(DlqQueue, DlxExchange, "#");

        // 2. Declare Main Exchange
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true);

        // 3. Declare Inbound Queue with DLX configuration
        var queueArgs = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = DlxExchange,
            ["x-dead-letter-routing-key"] = "opsbridge.dlq.goods-receipt"
        };

        await channel.QueueDeclareAsync(
            InboundQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs);

        await channel.QueueBindAsync(InboundQueue, Exchange, InboundRoutingKey);
    }
}
