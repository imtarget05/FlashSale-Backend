using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace OpsBridge.IntegrationTests;

[Collection("OpsBridgeIntegration")]
public sealed class RateLimitingIntegrationTests(OpsBridgeFixture fixture)
{
    [Fact]
    public async Task HostRateLimiter_Returns429AndRetryAfter_WhenLimitExceeded()
    {
        // Arrange: configure low limit for deterministic burst testing
        using var customFactory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Enabled"] = "true",
                    ["RateLimiting:OpsBridgePermitLimit"] = "3"
                });
            });
        });

        using var client = customFactory.CreateClient();

        // Act: send requests up to limit
        for (int i = 0; i < 3; i++)
        {
            using var okResponse = await client.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        }

        // Act: 4th request exceeds the limit
        using var rejectedResponse = await client.GetAsync("/healthz");

        // Assert: 429 Too Many Requests with RFC 7807 problem details
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
        Assert.True(rejectedResponse.Headers.Contains("Retry-After"), "Expected Retry-After header on 429 response");

        var retryAfterValue = rejectedResponse.Headers.GetValues("Retry-After").FirstOrDefault();
        Assert.NotNull(retryAfterValue);
        Assert.True(int.TryParse(retryAfterValue, out var seconds) && seconds > 0, "Expected positive Retry-After seconds");

        var body = await rejectedResponse.Content.ReadAsStringAsync();
        Assert.Contains("rfc7807", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Too Many Requests", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authoritative server-side rate limit exceeded", body, StringComparison.OrdinalIgnoreCase);
    }
}
