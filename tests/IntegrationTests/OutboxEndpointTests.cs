extern alias order;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlashSale.Application.Auth;
using FlashSale.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;

using Program = order.Program;

namespace FlashSale.IntegrationTests;

/// <summary>
/// The operator-facing outbox surface, exercised through the real HTTP
/// pipeline (ADR-019): POST /api/outbox/enqueue must answer 409 on a
/// duplicate MessageId instead of leaking the database's unique violation
/// (23505) as a 500.
/// </summary>
[Collection("flashsale")]
public sealed class OutboxEndpointTests : IAsyncLifetime
{
    private const string StaffPassword = "ops-only-password-from-config";

    /// <summary>Enables POST /api/auth/bootstrap for this host.</summary>
    private const string BootstrapToken = "integration-test-bootstrap-token";

    /// <summary>Must be >= 32 bytes, or JwtOptions refuses to start.</summary>
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";

    private const string StaffEmail = "staff@flashsale.local";
    private const string AgentRecoveryKey = "test-only-agent-recovery-hmac-key-at-least-32-bytes";

    private readonly FlashSaleFixture _fx;
    private readonly Dictionary<string, string?> _previousEnvironment = [];
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public OutboxEndpointTests(FlashSaleFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.ResetDatabaseAsync(stock: 25);

        ApplyEnvironment();
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        foreach (var (key, value) in _previousEnvironment)
            Environment.SetEnvironmentVariable(key, value);
        return Task.CompletedTask;
    }

    private void ApplyEnvironment()
    {
        var values = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ConnectionStrings__DefaultConnection"] = _fx.PostgresConnectionString,
            ["ConnectionStrings__Redis"] = _fx.RedisConnectionString,
            ["Messaging__Provider"] = "InMemory",
            ["Auth__Jwt__SigningKey"] = SigningKey,
            ["Bootstrap__StaffPassword"] = StaffPassword,
            ["Bootstrap__AdminToken"] = BootstrapToken,
            ["AgentRecovery__HmacKey"] = AgentRecoveryKey,
        };

        foreach (var (key, value) in values)
        {
            _previousEnvironment[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private HttpClient Authorized(string accessToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<string> StaffTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(StaffEmail, StaffPassword));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken;
    }

    [Fact]
    public async Task Enqueue_WithADuplicateMessageId_Answers409_Not500()
    {
        using var client = Authorized(await StaffTokenAsync());
        var messageId = Guid.NewGuid();
        var body = new { messageId, eventType = "order.created" };

        var first = await client.PostAsJsonAsync("/api/outbox/enqueue", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // The SECOND enqueue collides with the unique index
        // (IX_OutboxMessages_MessageId). That is a CONFLICT, not a server
        // fault: before the fix the raw 23505 escaped and the caller got a 500.
        var second = await client.PostAsJsonAsync("/api/outbox/enqueue", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var payload = await second.Content.ReadFromJsonAsync<DuplicateConflict>();
        Assert.NotNull(payload);
        Assert.Equal("duplicate_outbox_message_id", payload!.Error);
        Assert.Equal(messageId, payload.MessageId);

        // And the conflict did not create a second row.
        await using var db = _fx.CreateDbContext();
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .CountAsync(db.OutboxMessages.Where(m => m.MessageId == messageId));
        Assert.Equal(1, rows);
    }

    private sealed record DuplicateConflict(string Error, Guid MessageId);

    [Fact]
    public async Task ApprovedAgentCommand_RequeuesOnlyNamedDeadLetter_AndAuditsRetry()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var deadLetteredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await using (var db = _fx.CreateDbContext())
        {
            db.OutboxMessages.AddRange(
                new OutboxMessage { MessageId = firstId, EventType = "order.created", Topic = "orders", Payload = "{}", DeadLetteredAt = deadLetteredAt, RetryCount = 5 },
                new OutboxMessage { MessageId = secondId, EventType = "order.created", Topic = "orders", Payload = "{}", DeadLetteredAt = deadLetteredAt, RetryCount = 5 });
            await db.SaveChangesAsync();
        }

        var path = $"/internal/agent/outbox/{firstId}/requeue";
        var body = JsonSerializer.Serialize(new
        {
            intentId = "intent-specific-01",
            intentHash = new string('a', 64),
            idempotencyKey = "requeue-specific-01",
            approvedBy = "human-sre-01",
            expectedDeadLetteredAt = deadLetteredAt,
        });

        var unsigned = await _client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/internal/agent/outbox/stuck")).StatusCode);
        using var signedRead = SignedRequest("/internal/agent/outbox/stuck", "", HttpMethod.Get);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(signedRead)).StatusCode);

        using var alteredSignature = SignedRequest(path, body);
        alteredSignature.Headers.Remove("X-Agent-Signature");
        alteredSignature.Headers.Add("X-Agent-Signature", new string('0', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(alteredSignature)).StatusCode);

        var secondPath = $"/internal/agent/outbox/{secondId}/requeue";
        var staleBody = JsonSerializer.Serialize(new
        {
            intentId = "intent-stale-01", intentHash = new string('b', 64),
            idempotencyKey = "requeue-stale-01", approvedBy = "human-sre-01",
            expectedDeadLetteredAt = deadLetteredAt.AddSeconds(-1),
        });
        using var staleVersion = SignedRequest(secondPath, staleBody);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync(staleVersion)).StatusCode);

        using var signed = SignedRequest(path, body);
        var response = await _client.SendAsync(signed);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AgentRecoveryResponse>();
        Assert.Equal("requeued", result!.Status);
        Assert.Equal(firstId, result.MessageId);

        // A lost HTTP response followed by the same command returns the durable result.
        using var replay = SignedRequest(path, body);
        var replayResponse = await _client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replayResult = await replayResponse.Content.ReadFromJsonAsync<AgentRecoveryResponse>();
        Assert.Equal("already_applied", replayResult!.Status);

        using var sameKeyOtherMessage = SignedRequest(secondPath, body);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync(sameKeyOtherMessage)).StatusCode);

        await using var verify = _fx.CreateDbContext();
        var first = await verify.OutboxMessages.AsNoTracking().SingleAsync(x => x.MessageId == firstId);
        var second = await verify.OutboxMessages.AsNoTracking().SingleAsync(x => x.MessageId == secondId);
        Assert.Null(first.DeadLetteredAt);
        Assert.NotNull(second.DeadLetteredAt);
        Assert.Equal(1, await verify.Database.SqlQueryRaw<int>(
            "SELECT count(*)::integer AS \"Value\" FROM \"OutboxRecoveryCommands\" WHERE \"IdempotencyKey\" = 'requeue-specific-01'").SingleAsync());
    }

    [Fact]
    public async Task ApprovedAgentCommand_ConcurrentRequestsWithSameKey_HasNoDoubleEffect()
    {
        var targetId = Guid.NewGuid();
        var deadLetteredAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await using (var db = _fx.CreateDbContext())
        {
            db.OutboxMessages.Add(
                new OutboxMessage { MessageId = targetId, EventType = "order.created", Topic = "orders", Payload = "{}", DeadLetteredAt = deadLetteredAt, RetryCount = 5 });
            await db.SaveChangesAsync();
        }

        var path = $"/internal/agent/outbox/{targetId}/requeue";
        var body = JsonSerializer.Serialize(new
        {
            intentId = "intent-concurrent-01",
            intentHash = new string('c', 64),
            idempotencyKey = "requeue-concurrent-01",
            approvedBy = "human-sre-02",
            expectedDeadLetteredAt = deadLetteredAt,
        });

        // Fire 2 concurrent HTTP requests with the exact same idempotency key and payload
        var task1 = _client.SendAsync(SignedRequest(path, body));
        var task2 = _client.SendAsync(SignedRequest(path, body));
        var responses = await Task.WhenAll(task1, task2);

        // Both must return 200 OK (one 'requeued', the other 'already_applied')
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var results = new List<AgentRecoveryResponse>();
        foreach (var r in responses)
        {
            var res = await r.Content.ReadFromJsonAsync<AgentRecoveryResponse>();
            Assert.NotNull(res);
            results.Add(res!);
        }

        Assert.Contains(results, r => r.Status == "requeued");
        Assert.Contains(results, r => r.Status == "already_applied");

        // Verify only 1 audit command was written to the database
        await using var verify = _fx.CreateDbContext();
        var msg = await verify.OutboxMessages.AsNoTracking().SingleAsync(x => x.MessageId == targetId);
        Assert.Null(msg.DeadLetteredAt);
        Assert.Equal(1, await verify.Database.SqlQueryRaw<int>(
            "SELECT count(*)::integer AS \"Value\" FROM \"OutboxRecoveryCommands\" WHERE \"IdempotencyKey\" = 'requeue-concurrent-01'").SingleAsync());
    }

    private static HttpRequestMessage SignedRequest(string path, string body, HttpMethod? method = null)
    {
        method ??= HttpMethod.Post;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var signedPayload = $"{method.Method}\n{path}\n{timestamp}\n{bodyHash}";
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(AgentRecoveryKey), Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();
        var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Agent-Timestamp", timestamp);
        request.Headers.Add("X-Agent-Signature", signature);
        return request;
    }

    private sealed record AgentRecoveryResponse(string Status, Guid MessageId);
}
