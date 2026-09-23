using FlashSale.Application.Events;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Events;
using FlashSale.Infrastructure.Persistence;

namespace FlashSale.Infrastructure.Events;

/// <summary>
/// In-memory domain event publisher for development/testing (spec §1).
/// Routes events to registered handlers synchronously.
/// </summary>
public sealed class InMemoryDomainEventPublisher : IDomainEventPublisher, IAutomationEventPublisher
{
    private readonly List<Func<DomainEvent, CancellationToken, Task>> _handlers = new();

    public void AddHandler(Func<DomainEvent, CancellationToken, Task> handler) =>
        _handlers.Add(handler);

    public async Task PublishAsync(DomainEvent domainEvent, CancellationToken ct = default)
    {
        foreach (var handler in _handlers)
            await handler(domainEvent, ct);
    }

    public async Task PublishAsync(DomainEvent domainEvent, AutomationWorkflow workflow, CancellationToken ct = default)
    {
        foreach (var handler in _handlers)
            await handler(domainEvent, ct);
    }
}