namespace OpsBridge.Application.Security;

public interface IWebhookSignatureVerifier
{
    /// <summary>
    /// Verifies the webhook signature against the raw request body, timestamp header,
    /// and optional message ID according to official Svix and Standard Webhook specifications.
    /// Supports whsec_ Base64 secrets, multiple space-separated signatures, and constant-time equality.
    /// </summary>
    bool Verify(
        string rawBody,
        string signatureHeader,
        string timestampHeader,
        string secret,
        string? messageId = null);
}
