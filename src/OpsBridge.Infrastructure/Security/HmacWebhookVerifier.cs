using System.Security.Cryptography;
using System.Text;
using OpsBridge.Application.Security;
using OpsBridge.Domain.Exceptions;

namespace OpsBridge.Infrastructure.Security;

public sealed class HmacWebhookVerifier : IWebhookSignatureVerifier
{
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300); // 5 minutes
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(30);

    public bool Verify(
        string rawBody,
        string signatureHeader,
        string timestampHeader,
        string secret,
        string? messageId = null)
    {
        if (string.IsNullOrWhiteSpace(rawBody) ||
            string.IsNullOrWhiteSpace(signatureHeader) ||
            string.IsNullOrWhiteSpace(timestampHeader) ||
            string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        // 1. Anti-Replay: Validate timestamp
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

        // 2. Decode Secret: Official Svix secrets start with "whsec_" and are base64-encoded
        var secretBytes = ParseSecret(secret);

        // 3. Construct Payload to Sign:
        // Svix specification: $"{messageId}.{timestamp}.{rawBody}"
        // Fallback specification: $"{timestamp}.{rawBody}"
        var payloadToSignWithId = !string.IsNullOrWhiteSpace(messageId)
            ? $"{messageId}.{timestampHeader}.{rawBody}"
            : $"{timestampHeader}.{rawBody}";

        var payloadToSignFallback = $"{timestampHeader}.{rawBody}";

        using var hmac = new HMACSHA256(secretBytes);
        var computedHashWithId = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadToSignWithId));
        var computedHashFallback = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadToSignFallback));

        // 4. Svix headers can contain multiple signatures separated by spaces
        var signatureTokens = signatureHeader.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in signatureTokens)
        {
            var (version, sigValue) = ParseSignatureToken(token.Trim());
            if (string.IsNullOrWhiteSpace(sigValue)) continue;

            // Try decoding as Base64 (Svix standard)
            if (TryFromBase64(sigValue, out var candidateBytes))
            {
                if (CryptographicOperations.FixedTimeEquals(candidateBytes, computedHashWithId) ||
                    CryptographicOperations.FixedTimeEquals(candidateBytes, computedHashFallback))
                {
                    return true;
                }
            }

            // Try decoding as Hex (GitHub / custom style)
            if (TryFromHex(sigValue, out var hexCandidateBytes))
            {
                if (CryptographicOperations.FixedTimeEquals(hexCandidateBytes, computedHashWithId) ||
                    CryptographicOperations.FixedTimeEquals(hexCandidateBytes, computedHashFallback))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Helper to generate official Svix-compliant signature header: "v1,<base64>"
    /// </summary>
    public static string ComputeSvixSignatureHeader(string rawBody, string timestampHeader, string messageId, string secret)
    {
        var secretBytes = ParseSecret(secret);
        var payload = $"{messageId}.{timestampHeader}.{rawBody}";

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"v1,{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Helper to generate legacy/custom hex signature header: "sha256=<hex>"
    /// </summary>
    public static string ComputeLegacySignatureHeader(string rawBody, string timestampHeader, string secret, string prefix = "sha256=")
    {
        var secretBytes = ParseSecret(secret);
        var payload = $"{timestampHeader}.{rawBody}";

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"{prefix}{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static byte[] ParseSecret(string secret)
    {
        if (secret.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase))
        {
            var rawBase64 = secret["whsec_".Length..];
            try
            {
                return Convert.FromBase64String(rawBase64);
            }
            catch (FormatException)
            {
                // Fallback to UTF8 if not valid base64
                return Encoding.UTF8.GetBytes(secret);
            }
        }

        return Encoding.UTF8.GetBytes(secret);
    }

    private static (string version, string sigValue) ParseSignatureToken(string token)
    {
        if (token.StartsWith("v1,", StringComparison.OrdinalIgnoreCase))
        {
            return ("v1", token["v1,".Length..]);
        }
        if (token.StartsWith("v1=", StringComparison.OrdinalIgnoreCase))
        {
            return ("v1", token["v1=".Length..]);
        }
        if (token.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return ("sha256", token["sha256=".Length..]);
        }

        return ("raw", token);
    }

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length == 32; // HMAC-SHA256 is exactly 32 bytes
        }
        catch
        {
            bytes = [];
            return false;
        }
    }

    private static bool TryFromHex(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(value);
            return bytes.Length == 32;
        }
        catch
        {
            bytes = [];
            return false;
        }
    }

    private static bool TryParseTimestamp(string timestampStr, out DateTimeOffset timestamp)
    {
        if (DateTimeOffset.TryParse(timestampStr, out timestamp))
        {
            return true;
        }

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
