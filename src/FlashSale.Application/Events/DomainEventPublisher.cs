namespace FlashSale.Application.Events;

using FlashSale.Domain.Events;

public interface IDomainEventPublisher
{
    Task PublishAsync(DomainEvent @event, CancellationToken ct = default);
}

public sealed class FakeDomainEventPublisher : IDomainEventPublisher
{
    public readonly List<DomainEvent> PublishedEvents = new();
    public Task PublishAsync(DomainEvent @event, CancellationToken ct = default)
    {
        PublishedEvents.Add(@event);
        return Task.CompletedTask;
    }
}
