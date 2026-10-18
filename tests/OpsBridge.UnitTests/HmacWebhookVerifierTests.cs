using OpsBridge.Domain.Exceptions;
using OpsBridge.Infrastructure.Security;

namespace OpsBridge.UnitTests;

public class HmacWebhookVerifierTests
{
    private readonly HmacWebhookVerifier _verifier = new();
    private const string LegacySecret = "super-secret-key-for-testing-1234567890";
    private const string SvixSecret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw=";
    private const string Body = "{\"hello\":\"world\"}";
    private const string MessageId = "msg_20261018090000";

    [Fact]
    public void Verify_OfficialSvix_WhsecSecret_Base64Signature_ReturnsTrue()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSvixSignatureHeader(Body, timestamp, MessageId, SvixSecret);

        var result = _verifier.Verify(Body, signature, timestamp, SvixSecret, MessageId);

        Assert.True(result);
    }

    [Fact]
    public void Verify_Svix_MultipleSignaturesInHeader_OneMatches_ReturnsTrue()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var validSignature = HmacWebhookVerifier.ComputeSvixSignatureHeader(Body, timestamp, MessageId, SvixSecret);
        var multiSigHeader = $"v1,invalidBase64Sig12345678901234567890= {validSignature} v1,anotherFakeSig12345678901234567890=";

        var result = _verifier.Verify(Body, multiSigHeader, timestamp, SvixSecret, MessageId);

        Assert.True(result);
    }

    [Fact]
    public void Verify_LegacySha256Hex_ReturnsTrue()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeLegacySignatureHeader(Body, timestamp, LegacySecret, "sha256=");

        var result = _verifier.Verify(Body, signature, timestamp, LegacySecret);

        Assert.True(result);
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSvixSignatureHeader(Body, timestamp, MessageId, SvixSecret);

        var tamperedBody = "{\"hello\":\"hacked\"}";
        var result = _verifier.Verify(tamperedBody, signature, timestamp, SvixSecret, MessageId);

        Assert.False(result);
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSvixSignatureHeader(Body, timestamp, MessageId, SvixSecret);

        var wrongSecret = "whsec_000000000000000000000000000000000000000=";
        var result = _verifier.Verify(Body, signature, timestamp, wrongSecret, MessageId);

        Assert.False(result);
    }

    [Fact]
    public void Verify_ExpiredTimestamp_ThrowsTimestampExpiredException()
    {
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSvixSignatureHeader(Body, oldTimestamp, MessageId, SvixSecret);

        var ex = Assert.Throws<TimestampExpiredException>(() =>
            _verifier.Verify(Body, signature, oldTimestamp, SvixSecret, MessageId));

        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_FutureDriftTimestamp_ThrowsTimestampExpiredException()
    {
        var futureTimestamp = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds().ToString();
        var signature = HmacWebhookVerifier.ComputeSvixSignatureHeader(Body, futureTimestamp, MessageId, SvixSecret);

        var ex = Assert.Throws<TimestampExpiredException>(() =>
            _verifier.Verify(Body, signature, futureTimestamp, SvixSecret, MessageId));

        Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
