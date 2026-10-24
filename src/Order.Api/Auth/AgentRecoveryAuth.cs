using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Order.Api.Auth;

/// <summary>Authenticates a narrow Agent Hub command without sharing the user JWT signing key.</summary>
public static class AgentRecoveryAuth
{
    public static async Task<bool> VerifyAsync(HttpRequest request, string? sharedKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sharedKey) || Encoding.UTF8.GetByteCount(sharedKey) < 32)
            return false;
        if (!request.Headers.TryGetValue("X-Agent-Timestamp", out var rawTimestamp)
            || !long.TryParse(rawTimestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 300)
            return false;
        if (!request.Headers.TryGetValue("X-Agent-Signature", out var rawSignature))
            return false;

        byte[] supplied;
        try { supplied = Convert.FromHexString(rawSignature.ToString()); }
        catch (FormatException) { return false; }
        if (supplied.Length != 32)
            return false;

        request.EnableBuffering();
        if (request.ContentLength is > 4096)
            return false;
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        request.Body.Position = 0;
        if (buffer.Length > 4096)
            return false;

        var bodyHash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        var path = request.Path.Value ?? string.Empty;
        var signedPayload = $"{request.Method}\n{path}\n{timestamp}\n{bodyHash}";
        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(sharedKey), Encoding.UTF8.GetBytes(signedPayload));
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
