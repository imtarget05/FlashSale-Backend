using OpsBridge.Domain.Exceptions;
using OpsBridge.Infrastructure.Security;

namespace OpsBridge.UnitTests;

public class HmacWebhookVerifierTests
{
    private readonly HmacWebhookVerifier _verifier = new();
    private const string Secret = "super-secret-key-for-testing-1234567890";
    private const string Body = "{\"hello\":\"world\"}";

    [Fact]
    public void Verify_ValidSignature_Sha256Prefix_ReturnsTrue()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(Body, timestamp, Secret, "sha256=");

        var result = _verifier.Verify(Body, signature, timestamp, Secret);

        Assert.True(result);
    }

    [Fact]
    public void Verify_ValidSignature_SvixV1Prefix_ReturnsTrue()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(Body, timestamp, Secret, "v1=");

        var result = _verifier.Verify(Body, signature, timestamp, Secret);

        Assert.True(result);
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(Body, timestamp, Secret, "sha256=");

        var tamperedBody = "{\"hello\":\"hacked\"}";
        var result = _verifier.Verify(tamperedBody, signature, timestamp, Secret);

        Assert.False(result);
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(Body, timestamp, Secret, "sha256=");

        var result = _verifier.Verify(Body, signature, timestamp, "wrong-secret-key");

        Assert.False(result);
    }

    [Fact]
    public void Verify_ExpiredTimestamp_ThrowsTimestampExpiredException()
    {
        // 10 minutes ago (> 300s tolerance)
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(Body, oldTimestamp, Secret, "sha256=");

        var ex = Assert.Throws<TimestampExpiredException>(() =>
            _verifier.Verify(Body, signature, oldTimestamp, Secret));

        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_FutureDriftTimestamp_ThrowsTimestampExpiredException()
    {
        // 2 minutes in the future (> 30s future tolerance)
        var futureTimestamp = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(Body, futureTimestamp, Secret, "sha256=");

        var ex = Assert.Throws<TimestampExpiredException>(() =>
            _verifier.Verify(Body, signature, futureTimestamp, Secret));

        Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_Iso8601Timestamp_Succeeds()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("o");
        var signature = HmacWebhookVerifier.ComputeSignatureHeader(Body, timestamp, Secret, "v1=");

        var result = _verifier.Verify(Body, signature, timestamp, Secret);

        Assert.True(result);
    }
}
