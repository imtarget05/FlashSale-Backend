namespace FlashSale.Application.Auth;

using FlashSale.Domain;
using FlashSale.Domain.Entities;

public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);

public enum AuthError
{
    Validation,
    DuplicateEmail,
    InvalidCredentials,
    InvalidRefreshToken
}

public sealed record AuthResult(
    bool Succeeded,
    AuthError? Error,
    string? Message,
    IssuedTokens? Tokens);

public sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds);

public sealed record RefreshTokenPayload(Guid UserId, int Version);

public interface ITokenIssuer
{
    IssuedTokens Issue(User user);
    RefreshTokenPayload? ValidateRefreshToken(string refreshToken);
}

public interface IPbkdf2PasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);
}

public interface IUserStore
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> TryAddAsync(User user, CancellationToken ct = default);
    Task<bool> TryRotateTokenVersionAsync(Guid userId, int presentedVersion, CancellationToken ct = default);
    Task<int> BumpTokenVersionAsync(Guid userId, CancellationToken ct = default);
}

public sealed class BootstrapOptions
{
    public string? AdminToken { get; set; }
    public string? StaffPassword { get; set; }
    public string? StaffEmail { get; set; }

    public bool AdminTokenConfigured => !string.IsNullOrWhiteSpace(AdminToken);
    public bool StaffPasswordConfigured => !string.IsNullOrWhiteSpace(StaffPassword);

    public bool AcceptsAdminToken(string? presented) =>
        AdminTokenConfigured && string.Equals(AdminToken, presented, StringComparison.Ordinal);

    public static BootstrapOptions FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var section = configuration.GetSection("Bootstrap");
        return new BootstrapOptions
        {
            AdminToken = section["AdminToken"],
            StaffPassword = section["StaffPassword"],
            StaffEmail = section["StaffEmail"]
        };
    }
}
