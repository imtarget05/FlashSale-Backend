using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Order.Api.OpenApi;

/// <summary>
/// Documents the JWT bearer scheme in the OpenAPI document (Phase III).
/// </summary>
/// <remarks>
/// Without this, the contract is misleading: <c>/api/auth/me</c> and
/// <c>/orders/me</c> would appear as endpoints that can only ever return 401,
/// with no way for a generated client to attach a token. The transformer
/// describes what the server already enforces in Program.cs.
/// </remarks>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description =
                "Access token from POST /api/auth/login (or /register), sent as " +
                "'Authorization: Bearer <token>'. Valid for the lifetime reported by " +
                "expiresInSeconds (<= 15 minutes); use POST /api/auth/refresh to rotate.",
        };

        return Task.CompletedTask;
    }
}