using System.Security.Cryptography;
using System.Text;
using OpsBridge.Application.Security;
using OpsBridge.Domain.Exceptions;

namespace OpsBridge.Infrastructure.Security;

public sealed class HmacWebhookVerifier : IWebhookSignatureVerifier
{
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300); // 5 minutes
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(30);

    public bool Verify(string rawBody, string signatureHeader, string timestampHeader, string secret)
    {
        if (string.IsNullOrWhiteSpace(rawBody) ||
            string.IsNullOrWhiteSpace(signatureHeader) ||
            string.IsNullOrWhiteSpace(timestampHeader) ||
            string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        // Validate timestamp to prevent replay attacks
        if (!TryParseTimestamp(timestampHeader, out var timestamp))
        {
            throw new TimestampExpiredException("Invalid webhook timestamp format.");
        }

        var now = DateTimeOffset.UtcNow;
        if (now - timestamp > DefaultTolerance)
        {
            throw new TimestampExpiredException($"Webhook timestamp expired. Age: {(now - timestamp).TotalSeconds:F0}s (max allowed: {DefaultTolerance.TotalSeconds}s).");
        }

        if (timestamp - now > FutureTolerance)
        {
            throw new TimestampExpiredException($"Webhook timestamp is too far in the future: {(timestamp - now).TotalSeconds:F0}s.");
        }

        // Extract raw signature (support v1=..., sha256=..., or bare hex)
        var expectedHex = ExtractSignatureHex(signatureHeader);
        if (string.IsNullOrWhiteSpace(expectedHex))
        {
            return false;
        }

        // Canonical payload for signing: "{timestamp}.{body}"
        var payloadToSign = $"{timestampHeader}.{rawBody}";
        var computedHex = ComputeHmacSha256(payloadToSign, secret);

        // Constant-time comparison against timing attacks
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHex.ToLowerInvariant());
        var computedBytes = Encoding.UTF8.GetBytes(computedHex.ToLowerInvariant());

        if (expectedBytes.Length != computedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, computedBytes);
    }

    public static string ComputeSignatureHeader(string rawBody, string timestampHeader, string secret, string prefix = "sha256=")
    {
        var payloadToSign = $"{timestampHeader}.{rawBody}";
        var hex = ComputeHmacSha256(payloadToSign, secret);
        return $"{prefix}{hex}";
    }

    private static string ComputeHmacSha256(string content, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(contentBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ExtractSignatureHex(string header)
    {
        // Support Svix (v1,<hex> or v1=<hex>), GitHub (sha256=<hex>), or raw hex
        var parts = header.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["sha256=".Length..];
            }
            if (trimmed.StartsWith("v1=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["v1=".Length..];
            }
            if (trimmed.StartsWith("v1,", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["v1,".Length..];
            }
        }

        return header.Trim();
    }

    private static bool TryParseTimestamp(string timestampStr, out DateTimeOffset timestamp)
    {
        // 1. Try ISO 8601
        if (DateTimeOffset.TryParse(timestampStr, out timestamp))
        {
            return true;
        }

        // 2. Try Unix epoch seconds or milliseconds
        if (long.TryParse(timestampStr, out var epoch))
        {
            if (epoch > 100_000_000_000L) // milliseconds
            {
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(epoch);
                return true;
            }

            timestamp = DateTimeOffset.FromUnixTimeSeconds(epoch);
            return true;
        }

        timestamp = default;
        return false;
    }
}
