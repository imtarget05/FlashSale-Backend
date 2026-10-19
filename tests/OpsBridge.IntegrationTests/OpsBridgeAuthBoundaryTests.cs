using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using OpsBridge.Domain;
using OpsBridge.Infrastructure.Security;
using Xunit;

namespace OpsBridge.IntegrationTests;

[Collection("OpsBridgeIntegration")]
public sealed class OpsBridgeAuthBoundaryTests(OpsBridgeFixture fixture)
{
    [Fact]
    public async Task Production_DoesNotExpose_DemoRoleLogin()
    {
        using var productionFactory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SigningKey", "test-only-production-signing-key-with-32-bytes-minimum");
        });
        using var client = productionFactory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "attacker",
            role = "Admin",
            password = "anything"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Production_RequiresDedicatedJwtSigningKey()
    {
        using var productionFactory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] = null
                }));
        });

        var exception = Assert.Throws<InvalidOperationException>(() => productionFactory.CreateClient());

        Assert.Contains("Jwt:SigningKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_RejectsBuiltInJwtSigningKey()
    {
        using var productionFactory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SigningKey", OpsBridgeJwtTokenIssuer.DefaultSigningKey);
        });

        var exception = Assert.Throws<InvalidOperationException>(() => productionFactory.CreateClient());

        Assert.Contains("development key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpsBridgeRoutes_EnforceRoleScopes()
    {
        using var anonymous = fixture.AnonymousClient();
        using var anonymousResponse = await anonymous.GetAsync("/api/v1/connectors");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var customer = fixture.ClientForRole("CUSTOMER");
        using var customerResponse = await customer.GetAsync("/api/v1/connectors");
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);

        using var auditor = fixture.ClientForRole(OpsBridgeRoles.Auditor);
        using var auditorRead = await auditor.GetAsync("/api/v1/connectors");
        Assert.Equal(HttpStatusCode.OK, auditorRead.StatusCode);

        using var auditorWrite = await auditor.PostAsJsonAsync("/api/v1/connectors", new
        {
            name = "auditor-must-not-write",
            type = 1,
            secret = "not-persisted",
            baseUrl = "https://example.invalid",
            isActive = true
        });
        Assert.Equal(HttpStatusCode.Forbidden, auditorWrite.StatusCode);

        using var operatorClient = fixture.ClientForRole(OpsBridgeRoles.Operator);
        using var operatorWrite = await operatorClient.PostAsJsonAsync("/api/v1/connectors", new
        {
            name = $"operator-{Guid.NewGuid():N}",
            type = 1,
            secret = "connector-secret-test-value",
            baseUrl = "https://example.invalid",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.Created, operatorWrite.StatusCode);
        var responseBody = await operatorWrite.Content.ReadAsStringAsync();
        Assert.DoesNotContain("connector-secret-test-value", responseBody, StringComparison.Ordinal);
    }
}