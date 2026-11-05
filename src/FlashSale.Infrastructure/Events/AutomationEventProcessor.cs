namespace FlashSale.Infrastructure.Events;

using FlashSale.Application.Automation;
using FlashSale.Application.Outbox;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

public sealed class AutomationEventProcessor
{
    public const string ConsumerName = "flashsale.automation.v1";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutomationEventProcessor> _logger;

    public AutomationEventProcessor(IServiceScopeFactory scopeFactory, ILogger<AutomationEventProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public static string ReadStringHeader(byte[] header) => Encoding.UTF8.GetString(header ?? Array.Empty<byte>());

    public async Task<AutomationEventOutcome> ProcessAsync(byte[] body, string? eventTypeHeader)
    {
        string? eventType = null;
        Guid? messageId = null;

        try
        {
            using var doc = JsonDocument.Parse(body ?? Array.Empty<byte>());
            var root = doc.RootElement;
            eventType = root.TryGetProperty("eventType", out var et) ? et.GetString() : eventTypeHeader;
            if (root.TryGetProperty("eventId", out var ei) && Guid.TryParse(ei.GetString(), out var mid)) messageId = mid;
            if (root.TryGetProperty("correlationId", out var ci)) messageId ??= ci.GetGuid();
        }
        catch
        {
            return AutomationEventOutcome.Unreadable;
        }

        if (string.IsNullOrWhiteSpace(eventType)) return AutomationEventOutcome.Unreadable;

        if (messageId is not null)
        {
            using var scope = _scopeFactory.CreateScope();
            var inbox = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            if (await inbox.HasBeenProcessedAsync(messageId.Value, ConsumerName)) return AutomationEventOutcome.Duplicate;
            if (!await inbox.TryClaimAsync(messageId.Value, ConsumerName)) return AutomationEventOutcome.Duplicate;
        }

        using var runScope = _scopeFactory.CreateScope();
        var runs = runScope.ServiceProvider.GetRequiredService<IAutomationRunRepository>();
        var run = new AutomationRun
        {
            RunId = Guid.NewGuid().ToString("D"),
            Outcome = eventType,
            Status = AutomationRunStatus.Success,
            WorkflowName = MapWorkflow(eventType).ToString(),
            ResultSummary = $"Processed {eventType}.",
            LastRunAt = DateTimeOffset.UtcNow
        };
        await runs.CreateAsync(run);

        if (messageId is not null)
        {
            using var markScope = _scopeFactory.CreateScope();
            var inbox2 = markScope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await inbox2.MarkProcessedAsync(messageId.Value, ConsumerName);
        }

        return AutomationEventOutcome.Processed;
    }

    private static AutomationWorkflow MapWorkflow(string eventType) => eventType switch
    {
        "order.cancelled" => AutomationWorkflow.InventoryAutomation,
        "order.created" => AutomationWorkflow.InventoryAutomation,
        _ => AutomationWorkflow.InventoryAutomation
    };
}

public static class DomainEventServiceCollectionExtensions
{
    public static string ResolveProvider(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var events = configuration["Events:Provider"];
        if (!string.IsNullOrWhiteSpace(events)) return events;
        return configuration["Messaging:Provider"] ?? "RabbitMQ";
    }

    public static string ResolveKafkaTopic(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
        configuration["Events:KafkaTopic"] ?? "orders.events";
}
