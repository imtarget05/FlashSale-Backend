using FlashSale.Application.Auth;
using FlashSale.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace Order.Api.Auth;

/// <summary>
/// Auth endpoints (ADR-013). Thin by design: validation and decisions live in
/// <see cref="AuthService"/>, so this file only maps results to HTTP.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.RegisterAsync(request, ct);
            return result.Succeeded
                ? Results.Created("/api/auth/me", result.Tokens)
                : ToProblem(result);
        })
        .AllowAnonymous()
        .WithName("Register")
        .WithSummary("Create a CUSTOMER account and return a token pair.");

        group.MapPost("/login", async (LoginRequest request, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request, ct);
            return result.Succeeded ? Results.Ok(result.Tokens) : ToProblem(result);
        })
        .AllowAnonymous()
        .WithName("Login")
        .WithSummary("Exchange credentials for an access + refresh token pair.");

        group.MapPost("/refresh", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(request, ct);
            return result.Succeeded ? Results.Ok(result.Tokens) : ToProblem(result);
        })
        .AllowAnonymous()
        .WithName("Refresh")
        .WithSummary("Rotate a refresh token. A replayed token is rejected (401).");

        group.MapPost("/logout", async (ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
        {
            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
            if (!Guid.TryParse(sub, out var userId)) return Results.Unauthorized();

            await auth.LogoutAsync(userId, ct);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("Logout")
        .WithSummary("Invalidate the refresh token. The access token expires on its own (<= 15m).");

        // Cheap identity probe: proves the bearer token is accepted and shows
        // which claims the server actually sees.
        group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
            role = user.FindFirstValue(ClaimTypes.Role),
        }))
        .RequireAuthorization()
        .WithName("Me")
        .WithSummary("Return the identity carried by the current access token.");

        return app;
    }

    /// <summary>
    /// Map a typed auth error to a status code. 401 for bad credentials and bad
    /// refresh tokens (never 404 — that would confirm which emails exist).
    /// </summary>
    private static IResult ToProblem(AuthResult result) => result.Error switch
    {
        AuthError.Validation => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = [result.Message ?? "Invalid request."],
        }),
        AuthError.DuplicateEmail => Results.Conflict(new { error = result.Message }),
        AuthError.InvalidCredentials => Results.Json(new { error = result.Message }, statusCode: 401),
        AuthError.InvalidRefreshToken => Results.Json(new { error = result.Message }, statusCode: 401),
        _ => Results.Problem(result.Message),
    };
}