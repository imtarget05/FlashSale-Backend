using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsBridge.Domain.Connectors;
using OpsBridge.Domain.Ingestion;
using OpsBridge.Infrastructure.Persistence;
using OpsBridge.Infrastructure.Security;

namespace OpsBridge.IntegrationTests;

[CollectionDefinition("OpsBridgeIntegration")]
public class OpsBridgeCollection : ICollectionFixture<OpsBridgeFixture> { }

[Collection("OpsBridgeIntegration")]
public class WebhookIntegrationTests(OpsBridgeFixture fixture)
{
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw=";
    private const string EventIdStr = "a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d";

    private async Task SeedConnectorAsync()
    {
        await fixture.ResetAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();

        db.Connectors.Add(new ConnectorConfig
        {
            Id = Guid.NewGuid(),
            Name = "FactoryFlow Production Hook",
            Type = ConnectorType.FactoryFlowWebhook,
            Secret = Secret,
            IsActive = true
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PostWebhook_ValidSignedPayload_PersistsAtomicallyToPostgresAndReturns202()
    {
        await SeedConnectorAsync();
        var client = fixture.Factory.CreateClient();

        var payload = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSvixSignatureHeader(payload, timestamp, EventIdStr, Secret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/factoryflow")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("svix-id", EventIdStr);
        request.Headers.Add("svix-timestamp", timestamp);
        request.Headers.Add("svix-signature", signature);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Verify PostgreSQL atomic persistence: Event + Inbox + Outbox
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();

        var eventId = Guid.Parse(EventIdStr);

        // 1. IngestedEvents
        var savedEvent = await db.IngestedEvents.FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(savedEvent);
        Assert.Equal("GoodsReceiptConfirmed", savedEvent.EventType);
        Assert.Equal(EventProcessingStatus.Received, savedEvent.Status);

        // 2. InboxMessages
        var inboxEntry = await db.InboxMessages.FirstOrDefaultAsync(i => i.MessageId == eventId);
        Assert.NotNull(inboxEntry);
        Assert.Equal("webhook-ingestion", inboxEntry.ConsumerGroup);
        Assert.False(string.IsNullOrWhiteSpace(inboxEntry.PayloadHash));

        // 3. OutboxMessages
        var outboxEntry = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Payload.Contains(EventIdStr));
        Assert.NotNull(outboxEntry);
        Assert.Equal("Pending", outboxEntry.Status);
        Assert.Equal("opsbridge.events.inbound.goods-receipt", outboxEntry.Topic);
    }

    [Fact]
    public async Task PostWebhook_DuplicatePayload_Returns200WithDuplicateFlag()
    {
        await SeedConnectorAsync();
        var client = fixture.Factory.CreateClient();

        var payload = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSvixSignatureHeader(payload, timestamp, EventIdStr, Secret);

        // First attempt -> 202 Accepted
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/factoryflow")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        req1.Headers.Add("svix-id", EventIdStr);
        req1.Headers.Add("svix-timestamp", timestamp);
        req1.Headers.Add("svix-signature", signature);

        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Accepted, res1.StatusCode);

        // Second attempt with exact same payload -> 200 OK (idempotent duplicate)
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/factoryflow")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        req2.Headers.Add("svix-id", EventIdStr);
        req2.Headers.Add("svix-timestamp", timestamp);
        req2.Headers.Add("svix-signature", signature);

        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var json = await res2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Duplicate", json.GetProperty("status").GetString());

        // Verify only 1 row in IngestedEvents and OutboxMessages
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();
        var count = await db.IngestedEvents.CountAsync(e => e.EventId == Guid.Parse(EventIdStr));
        Assert.Equal(1, count);
        var outboxCount = await db.OutboxMessages.CountAsync(o => o.Payload.Contains(EventIdStr));
        Assert.Equal(1, outboxCount);
    }

    [Fact]
    public async Task PostWebhook_ConcurrentIdenticalWebhooks_HandledIdempotentlyWithoutOrphanedRows()
    {
        // P0 Verification: 10 parallel threads sending identical event simultaneously
        await SeedConnectorAsync();
        var client = fixture.Factory.CreateClient();

        var payload = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSvixSignatureHeader(payload, timestamp, EventIdStr, Secret);

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/factoryflow")
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };
            req.Headers.Add("svix-id", EventIdStr);
            req.Headers.Add("svix-timestamp", timestamp);
            req.Headers.Add("svix-signature", signature);
            return await client.SendAsync(req);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        var acceptedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Accepted);
        var duplicateCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        Assert.Equal(1, acceptedCount);
        Assert.Equal(9, duplicateCount);

        // Verify exactly 1 row exists in Postgres tables
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();

        var eventCount = await db.IngestedEvents.CountAsync(e => e.EventId == Guid.Parse(EventIdStr));
        var inboxCount = await db.InboxMessages.CountAsync(i => i.MessageId == Guid.Parse(EventIdStr));
        var outboxCount = await db.OutboxMessages.CountAsync(o => o.Payload.Contains(EventIdStr));

        Assert.Equal(1, eventCount);
        Assert.Equal(1, inboxCount);
        Assert.Equal(1, outboxCount);
    }

    [Fact]
    public async Task PostWebhook_TamperedSignature_Returns401Unauthorized()
    {
        await SeedConnectorAsync();
        var client = fixture.Factory.CreateClient();

        var payload = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var badSignature = "v1,0000000000000000000000000000000000000000000=";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/factoryflow")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("svix-id", EventIdStr);
        request.Headers.Add("svix-timestamp", timestamp);
        request.Headers.Add("svix-signature", badSignature);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_ExpiredTimestamp_Returns400BadRequest()
    {
        await SeedConnectorAsync();
        var client = fixture.Factory.CreateClient();

        var payload = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"));
        var expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSvixSignatureHeader(payload, expiredTimestamp, EventIdStr, Secret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/factoryflow")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("svix-id", EventIdStr);
        request.Headers.Add("svix-timestamp", expiredTimestamp);
        request.Headers.Add("svix-signature", signature);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
