using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsBridge.Domain;
using OpsBridge.Infrastructure.Security;

namespace OpsBridge.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth");

        group.MapPost("/login", (LoginRequest request, IOpsBridgeTokenIssuer tokenIssuer) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.BadRequest(new { error = "Username is required." });
            }

            // Supported default roles: Admin, Operator, FinanceReviewer, Auditor
            var role = request.Role ?? request.Username switch
            {
                var u when u.Contains("finance", StringComparison.OrdinalIgnoreCase) => OpsBridgeRoles.FinanceReviewer,
                var u when u.Contains("admin", StringComparison.OrdinalIgnoreCase) => OpsBridgeRoles.Admin,
                var u when u.Contains("auditor", StringComparison.OrdinalIgnoreCase) => OpsBridgeRoles.Auditor,
                _ => OpsBridgeRoles.Operator
            };

            var userId = request.Username.ToLowerInvariant();
            var token = tokenIssuer.IssueToken(userId, request.Username, role);

            return Results.Ok(new AuthResponse(
                AccessToken: token,
                TokenType: "Bearer",
                ExpiresIn: 28800, // 8 hours
                User: new UserDto(
                    Id: userId,
                    Username: request.Username,
                    Role: role,
                    Email: $"{userId}@opsbridge.internal"
                )
            ));
        });

        group.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            var username = principal.Identity?.Name ?? "Anonymous";
            var role = principal.FindFirstValue(ClaimTypes.Role) ?? principal.FindFirstValue("role") ?? "Viewer";
            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? username;

            return Results.Ok(new UserDto(id, username, role, $"{id}@opsbridge.internal"));
        }).RequireAuthorization();

        return app;
    }
}

public sealed record LoginRequest(string Username, string? Role = null, string? Password = null);
public sealed record UserDto(string Id, string Username, string Role, string Email);
public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn, UserDto User);
