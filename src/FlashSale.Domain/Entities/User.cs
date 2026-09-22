namespace FlashSale.Domain.Entities;

/// <summary>
/// An authenticated principal (ADR-013). Password material is stored as a
/// versioned PBKDF2 string, never as a plaintext or unsalted digest.
/// </summary>
/// <remarks>
/// <see cref="TokenVersion"/> is the refresh-token generation counter. It is
/// bumped atomically on every successful refresh and on logout, which is what
/// makes a replayed refresh token fail (see ADR-013 §3).
/// </remarks>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Normalized (trimmed, lower-cased) login identity; unique in the DB.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Encoded PBKDF2 string: <c>pbkdf2-sha256$iterations$salt$hash</c>.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>One of <see cref="AuthRoles"/>.</summary>
    public string Role { get; set; } = AuthRoles.Customer;

    /// <summary>Refresh-token generation. Rotated atomically; see ADR-013.</summary>
    public int TokenVersion { get; set; }

    public DateTime CreatedAt { get; set; }
}
