using FlashSale.Domain.Entities;

namespace FlashSale.Application.Auth;

/// <summary>A freshly minted token pair.</summary>
public sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds);

/// <summary>What a validated refresh token asserts about its owner.</summary>
public sealed record RefreshTokenPayload(Guid UserId, int TokenVersion);

/// <summary>
/// Port: mint and validate JWTs. Implemented in the API layer, because JWT
/// types live in a framework package that Application must not reference
/// (enforced by ArchitectureTests).
/// </summary>
public interface ITokenIssuer
{
    /// <summary>Mint an access + refresh pair for this user's current TokenVersion.</summary>
    IssuedTokens Issue(User user);

    /// <summary>
    /// Validate a refresh token's signature, expiry and token type.
    /// Returns <c>null</c> for anything invalid — callers must not distinguish
    /// "expired" from "forged" in the response.
    /// </summary>
    RefreshTokenPayload? ValidateRefreshToken(string refreshToken);
}

/// <summary>
/// Port: user persistence. The atomic rotation method is the load-bearing part
/// of ADR-013 §3 — it must be a single conditional UPDATE, not read-then-write.
/// </summary>
public interface IUserStore
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);

    Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Insert a new user. Returns false when the email already exists.</summary>
    Task<bool> TryAddAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Atomically rotate the refresh generation:
    /// <c>UPDATE Users SET TokenVersion = TokenVersion + 1 WHERE Id = @id AND TokenVersion = @presented</c>.
    /// Returns false when the presented version is stale — i.e. the token was
    /// already used (replay) or the user logged out.
    /// </summary>
    Task<bool> TryRotateTokenVersionAsync(Guid userId, int presentedVersion, CancellationToken ct = default);

    /// <summary>Bump the generation unconditionally (logout). Returns the new value.</summary>
    Task<int> BumpTokenVersionAsync(Guid userId, CancellationToken ct = default);
}