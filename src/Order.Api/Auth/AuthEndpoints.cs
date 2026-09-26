using FlashSale.Application.Auth;
using FlashSale.Domain;
using FlashSale.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics.Metrics;
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

        // First-admin bootstrap (ADR-013 §5). Anonymous by necessity — there is no
        // admin yet — and gated by an operator-held secret instead:
        //   * Bootstrap:AdminToken unset  -> 404. The endpoint does not exist, so
        //     a deployment that never configures it has no admin-creation path.
        //   * token presented but wrong  -> 403, compared in constant time.
        //   * token correct               -> creates ONE admin, then the operator
        //     unsets the variable (the startup warning says so).
        // It CREATES and never promotes an existing account, so it cannot be used
        // to seize an email somebody else already registered.
        group.MapPost("/bootstrap", async (
            BootstrapAdminRequest request,
            HttpRequest http,
            BootstrapOptions bootstrap,
            AuthService auth,
            CancellationToken ct) =>
        {
            if (!bootstrap.AdminTokenConfigured)
                return Results.NotFound(new { error = "Not found." });

            var presented = http.Headers["X-Bootstrap-Token"].ToString();
            if (!bootstrap.AcceptsAdminToken(presented))
                return Results.Json(new { error = "Invalid bootstrap token." }, statusCode: StatusCodes.Status403Forbidden);

            var result = await auth.BootstrapAdminAsync(new RegisterRequest(request.Email, request.Password), ct);
            Record(result, ApiMetrics.AuthRegistrations);
            return result.Succeeded
                ? Results.Created("/api/auth/me", result.Tokens)
                : ToProblem(result);
        })
        .AllowAnonymous()
        .WithName("BootstrapAdmin")
        .WithSummary("Create the initial ADMIN account. Requires X-Bootstrap-Token == Bootstrap:AdminToken; 404 when that variable is unset.");

        group.MapPost("/register", async (RegisterRequest request, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.RegisterAsync(request, ct);
            Record(result, ApiMetrics.AuthRegistrations);
            return result.Succeeded
                ? Results.Created("/api/auth/me", result.Tokens)
                : ToProblem(result);
        })
        .AllowAnonymous()
        .WithName("Register")
        .WithSummary("Create a CUSTOMER account and return a token pair. The role is always CUSTOMER — it is never read from the request body.");

        group.MapPost("/login", async (LoginRequest request, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request, ct);
            Record(result, ApiMetrics.AuthLogins);
            return result.Succeeded ? Results.Ok(result.Tokens) : ToProblem(result);
        })
        .AllowAnonymous()
        .WithName("Login")
        .WithSummary("Exchange credentials for an access + refresh token pair.");

        group.MapPost("/refresh", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(request, ct);
            Record(result, ApiMetrics.AuthRefreshes);
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
    /// Feed one auth attempt into the metrics surface (Phase III): the success
    /// counter for the operation, or the shared failure counter with a reason tag.
    /// </summary>
    /// <remarks>
    /// Kept here rather than in <c>AuthService</c> because Application must not
    /// reference the API's metrics type, and "how many logins failed" is a
    /// transport-level question the endpoint is the right place to answer.
    /// </remarks>
    private static void Record(AuthResult result, Counter<long>? successCounter)
    {
        if (result.Succeeded)
        {
            successCounter?.Add(1);
            return;
        }

        ApiMetrics.AuthFailed(result.Error switch
        {
            AuthError.DuplicateEmail => "duplicate_email",
            AuthError.InvalidCredentials => "invalid_credentials",
            // Replay and forgery are the same answer to the client; the tag keeps
            // them distinguishable on the metrics surface.
            AuthError.InvalidRefreshToken => "invalid_refresh_token",
            AuthError.Validation => "validation",
            _ => "unknown",
        });
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

/// <summary>Body for <c>POST /api/auth/bootstrap</c>.</summary>
public sealed record BootstrapAdminRequest(string Email, string Password);