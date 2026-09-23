using FlashSale.Domain.Automation;
using FlashSale.Domain.Events;

namespace FlashSale.Application.Events;

/// <summary>Contract for publishing domain/automation events to the queue (spec §1).</summary>
public interface IDomainEventPublisher
{
    Task PublishAsync(DomainEvent domainEvent, CancellationToken ct = default);
}

/// <summary>
/// Extended publisher for automation-specific events that need workflow context
/// (spec §11 audit, §12 retry).
/// </summary>
public interface IAutomationEventPublisher
{
    Task PublishAsync(DomainEvent domainEvent, AutomationWorkflow workflow, CancellationToken ct = default);
}