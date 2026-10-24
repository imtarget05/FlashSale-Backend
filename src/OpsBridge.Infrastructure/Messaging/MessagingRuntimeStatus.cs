namespace OpsBridge.Infrastructure.Messaging;

/// <summary>Broker consumer status for a separate dependency probe and alerts.</summary>
public sealed class MessagingRuntimeStatus
{
    private int _consumerConnected;
    public bool ConsumerConnected => Volatile.Read(ref _consumerConnected) == 1;
    internal void SetConsumerConnected(bool connected) =>
        Volatile.Write(ref _consumerConnected, connected ? 1 : 0);
}
