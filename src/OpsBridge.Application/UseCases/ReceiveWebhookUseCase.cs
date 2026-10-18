using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using OpsBridge.Application.Common;
using OpsBridge.Application.Connectors;
using OpsBridge.Application.Contracts;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Application.Ingestion;
using OpsBridge.Application.Security;
using OpsBridge.Domain.Connectors;
using OpsBridge.Domain.Contracts;
using OpsBridge.Domain.Exceptions;
using OpsBridge.Domain.InboxOutbox;
using OpsBridge.Domain.Ingestion;

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
        new(true, false, eventId, "Event accepted, recorded in Inbox/Outbox, and staged for processing", contract);

    public static WebhookIngestionResult Duplicate(Guid eventId) =>
        new(true, true, eventId, "Duplicate event ignored idempotently");
}

public sealed class ReceiveWebhookUseCase(
    IConnectorRepository connectorRepository,
    IWebhookSignatureVerifier signatureVerifier,
    IContractValidator contractValidator,
    IInboxRepository inboxRepository,
    IIngestedEventRepository ingestedEventRepository,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork,
    ILogger<ReceiveWebhookUseCase> logger)
{
    public const string ConsumerGroup = "webhook-ingestion";
    public const string InboundTopic = "opsbridge.events.inbound.goods-receipt";

    public async Task<WebhookIngestionResult> ExecuteAsync(
        string rawBody,
        string? signatureHeader,
        string? timestampHeader,
        string? messageIdHeader = null,
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
            connector.Secret,
            messageIdHeader);

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

        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

        // 1. Fast read deduplication check
        var existingInbox = await inboxRepository.GetMessageAsync(contract.EventId, ConsumerGroup, ct);
        if (existingInbox != null)
        {
            if (string.Equals(existingInbox.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Event {EventId} already recorded with matching payload. Deduplicating idempotently.", contract.EventId);
                return WebhookIngestionResult.Duplicate(contract.EventId);
            }

            logger.LogWarning("Event {EventId} payload conflict! Existing hash: {Existing}, Incoming hash: {Incoming}",
                contract.EventId, existingInbox.PayloadHash, payloadHash);
            throw new EventPayloadConflictException(contract.EventId, $"Event with ID '{contract.EventId}' already exists with a different payload.");
        }

        var existingEvent = await ingestedEventRepository.GetByIdAsync(contract.EventId, ct);
        if (existingEvent != null)
        {
            var existingEventHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(existingEvent.RawPayload))).ToLowerInvariant();
            if (string.Equals(existingEventHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Event {EventId} already recorded in IngestedEvents with matching payload. Deduplicating idempotently.", contract.EventId);
                return WebhookIngestionResult.Duplicate(contract.EventId);
            }

            logger.LogWarning("Event {EventId} payload conflict against IngestedEvent! Existing hash: {Existing}, Incoming hash: {Incoming}",
                contract.EventId, existingEventHash, payloadHash);
            throw new EventPayloadConflictException(contract.EventId, $"Event with ID '{contract.EventId}' already exists with a different payload.");
        }

        // 2. Atomic Transaction (Inbox + IngestedEvent + Outbox) to satisfy P0 Atomicity
        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Add IngestedEvent
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

            // Add InboxMessage
            var inboxEntry = new InboxMessage
            {
                MessageId = contract.EventId,
                ConsumerGroup = ConsumerGroup,
                EventType = contract.EventType,
                Source = contract.Source,
                PayloadHash = payloadHash,
                ProcessedAt = DateTimeOffset.UtcNow
            };
            await inboxRepository.AddAsync(inboxEntry, ct);

            // Add OutboxMessage for guaranteed reliable delivery
            var outboxMessage = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = contract.EventType,
                Topic = InboundTopic,
                Payload = rawBody,
                Status = "Pending",
                CreatedAt = DateTimeOffset.UtcNow,
                RetryCount = 0
            };
            await outboxRepository.AddAsync(outboxMessage, ct);

            await tx.CommitAsync(ct);

            logger.LogInformation("Event {EventId} atomically committed (Event, Inbox, Outbox).", contract.EventId);
            return WebhookIngestionResult.Accepted(contract.EventId, contract);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);
            var committed = await inboxRepository.GetMessageAsync(contract.EventId, ConsumerGroup, ct);
            if (committed != null && !string.Equals(committed.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new EventPayloadConflictException(contract.EventId, $"Concurrent race detected conflicting payload for Event ID '{contract.EventId}'.");
            }

            logger.LogInformation("Concurrent duplicate detected via DB unique constraint for Event {EventId}. Returning duplicate.", contract.EventId);
            return WebhookIngestionResult.Duplicate(contract.EventId);
        }
    }

    private static bool IsUniqueViolation(Exception ex)
    {
        var msg = ex.ToString();
        return msg.Contains("23505", StringComparison.OrdinalIgnoreCase) // Postgres unique_violation code
            || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase);
    }
}
