using Microsoft.Extensions.Logging.Abstractions;
using OpsBridge.Application.Connectors;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Application.Ingestion;
using OpsBridge.Application.UseCases;
using OpsBridge.Domain.Connectors;
using OpsBridge.Domain.Exceptions;
using OpsBridge.Domain.Ingestion;
using OpsBridge.Infrastructure.Contracts;
using OpsBridge.Infrastructure.Security;

namespace OpsBridge.UnitTests;

public class ReceiveWebhookUseCaseTests
{
    private readonly FakeConnectorRepository _connectorRepo = new();
    private readonly FakeInboxRepository _inboxRepo = new();
    private readonly FakeIngestedEventRepository _eventRepo = new();
    private readonly HmacWebhookVerifier _verifier = new();
    private readonly JsonContractValidator _contractValidator = new();
    private readonly ReceiveWebhookUseCase _useCase;

    private const string Secret = "ff-webhook-secret-98765";
    private readonly string _validPayload;

    public ReceiveWebhookUseCaseTests()
    {
        _useCase = new ReceiveWebhookUseCase(
            _connectorRepo,
            _verifier,
            _contractValidator,
            _inboxRepo,
            _eventRepo,
            NullLogger<ReceiveWebhookUseCase>.Instance);

        _connectorRepo.SaveAsync(new ConnectorConfig
        {
            Id = Guid.NewGuid(),
            Name = "FactoryFlow Production",
            Type = ConnectorType.FactoryFlowWebhook,
            Secret = Secret,
            IsActive = true
        }).Wait();

        _validPayload = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"));
    }

    [Fact]
    public async Task Execute_ValidWebhook_IngestsSuccessfully()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(_validPayload, timestamp, Secret, "v1=");

        var result = await _useCase.ExecuteAsync(
            _validPayload,
            signature,
            timestamp,
            ConnectorType.FactoryFlowWebhook);

        Assert.True(result.IsAccepted);
        Assert.False(result.IsDuplicate);
        Assert.NotNull(result.Contract);
        Assert.Equal("GoodsReceiptConfirmed", result.Contract.EventType);

        // Verify stored in IngestedEventRepository
        Assert.True(await _eventRepo.ExistsAsync(result.EventId));

        // Verify marked in Inbox
        Assert.True(await _inboxRepo.HasBeenProcessedAsync(result.EventId, "webhook-ingestion"));
    }

    [Fact]
    public async Task Execute_DuplicateWebhook_ReturnsDuplicateIdempotently()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(_validPayload, timestamp, Secret, "v1=");

        // First call
        var firstResult = await _useCase.ExecuteAsync(
            _validPayload,
            signature,
            timestamp,
            ConnectorType.FactoryFlowWebhook);

        Assert.True(firstResult.IsAccepted);
        Assert.False(firstResult.IsDuplicate);

        // Second call with same event
        var secondResult = await _useCase.ExecuteAsync(
            _validPayload,
            signature,
            timestamp,
            ConnectorType.FactoryFlowWebhook);

        Assert.True(secondResult.IsAccepted);
        Assert.True(secondResult.IsDuplicate);
        Assert.Equal(firstResult.EventId, secondResult.EventId);
    }

    [Fact]
    public async Task Execute_InvalidSignature_ThrowsSignatureValidationException()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var badSignature = "v1=0000000000000000000000000000000000000000000000000000000000000000";

        await Assert.ThrowsAsync<SignatureValidationException>(() =>
            _useCase.ExecuteAsync(
                _validPayload,
                badSignature,
                timestamp,
                ConnectorType.FactoryFlowWebhook));
    }

    [Fact]
    public async Task Execute_InvalidContractPayload_ThrowsInvalidEventContractException()
    {
        var invalidPayload = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "invalid-goods-receipt-v1-missing-fields.json"));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(invalidPayload, timestamp, Secret, "v1=");

        await Assert.ThrowsAsync<InvalidEventContractException>(() =>
            _useCase.ExecuteAsync(
                invalidPayload,
                signature,
                timestamp,
                ConnectorType.FactoryFlowWebhook));
    }

    [Fact]
    public async Task Execute_InactiveConnector_ThrowsDomainException()
    {
        var connector = await _connectorRepo.GetByTypeAsync(ConnectorType.FactoryFlowWebhook);
        connector!.IsActive = false;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(_validPayload, timestamp, Secret, "v1=");

        await Assert.ThrowsAsync<DomainException>(() =>
            _useCase.ExecuteAsync(
                _validPayload,
                signature,
                timestamp,
                ConnectorType.FactoryFlowWebhook));
    }
}

// -------------------------------------------------------------
// In-Memory Fakes for pure Unit Testing without external DB
// -------------------------------------------------------------
internal sealed class FakeConnectorRepository : IConnectorRepository
{
    private readonly List<ConnectorConfig> _list = [];

    public Task<ConnectorConfig?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_list.FirstOrDefault(c => c.Id == id));

    public Task<ConnectorConfig?> GetByTypeAsync(ConnectorType type, CancellationToken ct = default) =>
        Task.FromResult(_list.FirstOrDefault(c => c.Type == type && c.IsActive));

    public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConnectorConfig>>(_list.ToList());

    public Task SaveAsync(ConnectorConfig connector, CancellationToken ct = default)
    {
        _list.RemoveAll(c => c.Id == connector.Id || c.Type == connector.Type);
        _list.Add(connector);
        return Task.CompletedTask;
    }
}

internal sealed class FakeInboxRepository : IInboxRepository
{
    private readonly HashSet<(Guid, string)> _processed = [];

    public Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerGroup = "default", CancellationToken ct = default) =>
        Task.FromResult(_processed.Contains((messageId, consumerGroup)));

    public Task MarkProcessedAsync(Guid messageId, string consumerGroup, string eventType, string source, CancellationToken ct = default)
    {
        _processed.Add((messageId, consumerGroup));
        return Task.CompletedTask;
    }
}

internal sealed class FakeIngestedEventRepository : IIngestedEventRepository
{
    private readonly Dictionary<Guid, IngestedEvent> _events = [];

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default) =>
        Task.FromResult(_events.ContainsKey(eventId));

    public Task AddAsync(IngestedEvent ev, CancellationToken ct = default)
    {
        _events[ev.EventId] = ev;
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(Guid eventId, EventProcessingStatus status, string? errorMessage = null, CancellationToken ct = default)
    {
        if (_events.TryGetValue(eventId, out var ev))
        {
            ev.Status = status;
            ev.ErrorMessage = errorMessage;
        }
        return Task.CompletedTask;
    }

    public Task<IngestedEvent?> GetByIdAsync(Guid eventId, CancellationToken ct = default) =>
        Task.FromResult(_events.GetValueOrDefault(eventId));

    public Task<IReadOnlyList<IngestedEvent>> GetRecentAsync(int limit = 50, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IngestedEvent>>(_events.Values.OrderByDescending(e => e.ReceivedAt).Take(limit).ToList());
}
