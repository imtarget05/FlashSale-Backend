using Microsoft.Extensions.Logging;
using OpsBridge.Domain.Connectors;
using OpsBridge.Domain.Contracts;
using OpsBridge.Domain.Exceptions;
using OpsBridge.Domain.Ingestion;
using OpsBridge.Application.Connectors;
using OpsBridge.Application.Contracts;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Application.Ingestion;
using OpsBridge.Application.Security;

namespace OpsBridge.Application.UseCases;

public sealed record WebhookIngestionResult(
    bool IsAccepted,
    bool IsDuplicate,
    Guid EventId,
    string Message,
    GoodsReceiptConfirmedV1? Contract = null
)
{
    public static WebhookIngestionResult Accepted(Guid eventId, GoodsReceiptConfirmedV1 contract) =>
        new(true, false, eventId, "Event accepted and staged for processing", contract);

    public static WebhookIngestionResult Duplicate(Guid eventId) =>
        new(true, true, eventId, "Duplicate event ignored idempotently");
}

public sealed class ReceiveWebhookUseCase(
    IConnectorRepository connectorRepository,
    IWebhookSignatureVerifier signatureVerifier,
    IContractValidator contractValidator,
    IInboxRepository inboxRepository,
    IIngestedEventRepository ingestedEventRepository,
    ILogger<ReceiveWebhookUseCase> logger)
{
    private const string ConsumerGroup = "webhook-ingestion";

    public async Task<WebhookIngestionResult> ExecuteAsync(
        string rawBody,
        string? signatureHeader,
        string? timestampHeader,
        ConnectorType connectorType = ConnectorType.FactoryFlowWebhook,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            throw new InvalidEventContractException("Request body cannot be empty");
        }

        var connector = await connectorRepository.GetByTypeAsync(connectorType, ct)
            ?? throw new DomainException($"Active connector of type '{connectorType}' was not found.");

        if (!connector.IsActive)
        {
            throw new DomainException($"Connector '{connector.Name}' is currently inactive.");
        }

        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(timestampHeader))
        {
            throw new SignatureValidationException("Missing required webhook signature or timestamp headers.");
        }

        var isValidSignature = signatureVerifier.Verify(
            rawBody,
            signatureHeader,
            timestampHeader,
            connector.Secret);

        if (!isValidSignature)
        {
            logger.LogWarning("Webhook signature verification failed for connector {ConnectorName}", connector.Name);
            throw new SignatureValidationException("Webhook signature verification failed.");
        }

        var validationResult = contractValidator.ValidateGoodsReceiptConfirmedV1(rawBody);
        if (!validationResult.IsValid || validationResult.Contract == null)
        {
            logger.LogWarning("Contract validation failed: {Error}", validationResult.ErrorMessage);
            throw new InvalidEventContractException(validationResult.ErrorMessage ?? "Payload does not conform to GoodsReceiptConfirmed v1 contract.");
        }

        var contract = validationResult.Contract;

        // Durable Deduplication check via Inbox
        var isDuplicate = await inboxRepository.HasBeenProcessedAsync(contract.EventId, ConsumerGroup, ct)
            || await ingestedEventRepository.ExistsAsync(contract.EventId, ct);

        if (isDuplicate)
        {
            logger.LogInformation("Event {EventId} has already been processed. Deduplicating idempotently.", contract.EventId);
            return WebhookIngestionResult.Duplicate(contract.EventId);
        }

        // Persist ingested event
        var ingestedEvent = new IngestedEvent
        {
            EventId = contract.EventId,
            EventType = contract.EventType,
            SchemaVersion = contract.SchemaVersion,
            Source = contract.Source,
            OccurredAt = contract.OccurredAt,
            CorrelationId = contract.CorrelationId,
            RawPayload = rawBody,
            Status = EventProcessingStatus.Received,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        await ingestedEventRepository.AddAsync(ingestedEvent, ct);

        // Mark processed in Inbox
        await inboxRepository.MarkProcessedAsync(
            contract.EventId,
            ConsumerGroup,
            contract.EventType,
            contract.Source,
            ct);

        logger.LogInformation("Event {EventId} ({EventType}) successfully ingested and recorded in Inbox.",
            contract.EventId, contract.EventType);

        return WebhookIngestionResult.Accepted(contract.EventId, contract);
    }
}
