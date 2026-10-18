namespace OpsBridge.Application.Messaging;

public interface IEventPublisher
{
    /// <summary>
    /// Publishes a message to a topic exchange with RabbitMQ Publisher Confirms enabled.
    /// Returns true when the broker confirms persistent receipt.
    /// </summary>
    Task<bool> PublishAsync(
        string topic,
        string payload,
        IDictionary<string, object?>? headers = null,
        CancellationToken ct = default);
}
