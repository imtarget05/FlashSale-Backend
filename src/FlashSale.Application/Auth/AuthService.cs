using FlashSale.Domain;
using FlashSale.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Auth;

/// <summary>
/// Use case: register, login, refresh, logout (ADR-013).
/// </summary>
/// <remarks>
/// Depends only on ports, so the whole auth surface is unit-testable with no
/// database, no JWT library and no web host.
/// </remarks>
public sealed class AuthService(
    IUserStore users,
    IPasswordHasher hasher,
    ITokenIssuer tokens,
    ILogger<AuthService> logger)
{
    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (!AuthValidation.IsValidEmail(request.Email))
            return AuthResult.Failure(AuthError.Validation, "A valid email address is required.");

        if (!AuthValidation.IsValidPassword(request.Password))
            return AuthResult.Failure(
                AuthError.Validation,
                $"Password must be at least {AuthValidation.MinPasswordLength} characters.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = AuthValidation.NormalizeEmail(request.Email),
            PasswordHash = hasher.Hash(request.Password),
            Role = AuthRoles.Customer,
            TokenVersion = 0,
            CreatedAt = DateTime.UtcNow,
        };

        if (!await users.TryAddAsync(user, ct))
        {
            // Deliberately explicit rather than a silent success: the caller is a
            // registration form, and "this email is taken" is actionable.
            logger.LogInformation("Registration rejected: email already registered.");
            return AuthResult.Failure(AuthError.DuplicateEmail, "That email is already registered.");
        }

        logger.LogInformation("Registered user {UserId} with role {Role}.", user.Id, user.Role);
        return AuthResult.Success(ToResponse(user, tokens.Issue(user)));
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var normalized = AuthValidation.NormalizeEmail(request.Email);
        var user = await users.FindByEmailAsync(normalized, ct);

        // One failure path for "unknown email" and "wrong password": distinguishing
        // them would turn the endpoint into an account-enumeration oracle.
        if (user is null || !hasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
        {
            logger.LogInformation("Login failed for an unknown email or bad password.");
            return AuthResult.Failure(AuthError.InvalidCredentials, "Invalid email or password.");
        }

        logger.LogInformation("Login succeeded for user {UserId}.", user.Id);
        return AuthResult.Success(ToResponse(user, tokens.Issue(user)));
    }

    /// <summary>
    /// Exchange a refresh token for a new pair, rotating the generation.
    /// </summary>
    /// <remarks>
    /// The rotation is a single conditional UPDATE. Two concurrent refreshes
    /// therefore cannot both succeed: the first increments the version, the
    /// second matches zero rows and is rejected. A read-validate-write sequence
    /// would let both through and make a stolen token replayable.
    /// </remarks>
    public async Task<AuthResult> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var payload = tokens.ValidateRefreshToken(request.RefreshToken ?? string.Empty);
        if (payload is null)
            return AuthResult.Failure(AuthError.InvalidRefreshToken, "Invalid or expired refresh token.");

        if (!await users.TryRotateTokenVersionAsync(payload.UserId, payload.TokenVersion, ct))
        {
            logger.LogWarning(
                "Refresh rejected for user {UserId}: token generation {Version} is stale (replay or logout).",
                payload.UserId, payload.TokenVersion);
            return AuthResult.Failure(AuthError.InvalidRefreshToken, "Invalid or expired refresh token.");
        }

        var user = await users.FindByIdAsync(payload.UserId, ct);
        if (user is null)
            return AuthResult.Failure(AuthError.InvalidRefreshToken, "Invalid or expired refresh token.");

        logger.LogInformation("Refresh rotated for user {UserId} to generation {Version}.", user.Id, user.TokenVersion);
        return AuthResult.Success(ToResponse(user, tokens.Issue(user)));
    }

    /// <summary>
    /// Invalidate the refresh generation. The caller's existing access token
    /// stays valid until its own expiry (<= 15 minutes) — see ADR-013 §4.
    /// </summary>
    public async Task<bool> LogoutAsync(Guid userId, CancellationToken ct = default)
    {
        var version = await users.BumpTokenVersionAsync(userId, ct);
        logger.LogInformation("Logout for user {UserId}; refresh generation is now {Version}.", userId, version);
        return true;
    }

    private static AuthResponse ToResponse(User user, IssuedTokens issued) =>
        new(issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds, user.Email, user.Role);
}