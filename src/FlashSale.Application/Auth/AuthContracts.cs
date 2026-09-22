namespace FlashSale.Application.Auth;

// ---------------------------------------------------------------
// Auth contracts (ADR-013). Plain records, no framework attributes:
// the Application layer is BCL-only (enforced by ArchitectureTests).
// ---------------------------------------------------------------

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string Email,
    string Role);

/// <summary>Why an auth operation failed — mapped to HTTP status by the API layer.</summary>
public enum AuthError
{
    None = 0,

    /// <summary>Register with an email that already exists.</summary>
    DuplicateEmail,

    /// <summary>Login with an unknown email or a wrong password.</summary>
    InvalidCredentials,

    /// <summary>Refresh token is malformed, expired, wrong-type, or replayed.</summary>
    InvalidRefreshToken,

    /// <summary>Email/password failed shape validation.</summary>
    Validation,
}

/// <summary>Result of an auth operation: either a token pair or a typed error.</summary>
public sealed record AuthResult(AuthResponse? Tokens, AuthError Error, string? Message = null)
{
    public bool Succeeded => Error == AuthError.None && Tokens is not null;

    public static AuthResult Success(AuthResponse tokens) => new(tokens, AuthError.None);

    public static AuthResult Failure(AuthError error, string message) => new(null, error, message);
}

/// <summary>Validation shared by register and login so both reject the same shapes.</summary>
public static class AuthValidation
{
    public const int MinPasswordLength = 10;

    /// <summary>Upper bounds are a DoS guard: PBKDF2 cost scales with input size.</summary>
    public const int MaxPasswordLength = 256;
    public const int MaxEmailLength = 254;

    /// <summary>
    /// Normalize once, at the edge, so duplicates cannot hide behind casing or
    /// surrounding whitespace ("A@x.com" vs "a@x.com " must be the same account).
    /// </summary>
    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>True when the email is a plausible address within the length bound.</summary>
    public static bool IsValidEmail(string? email)
    {
        var normalized = NormalizeEmail(email);
        if (normalized.Length is 0 or > MaxEmailLength) return false;

        // Deliberately shape-only: full RFC 5322 validation is not a security
        // control, and a hand-rolled regex would reject addresses that are valid.
        var at = normalized.IndexOf('@');
        if (at <= 0 || at != normalized.LastIndexOf('@')) return false;
        if (at == normalized.Length - 1) return false;

        var domain = normalized[(at + 1)..];
        return domain.Contains('.')
            && !domain.StartsWith('.')
            && !domain.EndsWith('.')
            && !normalized.Contains(' ');
    }

    /// <summary>True when the password meets the minimum length policy.</summary>
    public static bool IsValidPassword(string? password) =>
        password is { Length: >= MinPasswordLength and <= MaxPasswordLength };
}