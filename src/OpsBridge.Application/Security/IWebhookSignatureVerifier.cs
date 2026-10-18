namespace OpsBridge.Application.Security;

public interface IWebhookSignatureVerifier
{
    /// <summary>
    /// Verifies the webhook signature against the raw request body and timestamp header.
    /// Supports both Svix standard headers and OpsBridge custom headers with anti-replay protection.
    /// </summary>
    bool Verify(string rawBody, string signatureHeader, string timestampHeader, string secret);
}
