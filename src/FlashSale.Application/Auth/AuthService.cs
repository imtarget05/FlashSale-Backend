namespace FlashSale.Application.Auth;

using FlashSale.Domain;
using FlashSale.Domain.Entities;
using Microsoft.Extensions.Logging;

public sealed class AuthService
{
    private readonly IUserStore _store;
    private readonly IPbkdf2PasswordHasher _hasher;
    private readonly ITokenIssuer _issuer;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserStore store,
        IPbkdf2PasswordHasher hasher,
        ITokenIssuer issuer,
        ILogger<AuthService> logger)
    {
        _store = store;
        _hasher = hasher;
        _issuer = issuer;
        _logger = logger;
    }

    public Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default) =>
        RegisterCore(request, nameof(AuthRoles.Customer), ct);

    public Task<AuthResult> BootstrapAdminAsync(RegisterRequest request, CancellationToken ct = default) =>
        RegisterCore(request, nameof(AuthRoles.Admin), ct);

    private async Task<AuthResult> RegisterCore(RegisterRequest request, string role, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        if (!IsValidEmail(email) || request.Password is null or { Length: < 8 })
            return new AuthResult(false, AuthError.Validation, "Invalid email or password", null);

        if (await _store.TryAddAsync(new User { Email = email, PasswordHash = _hasher.Hash(request.Password), Role = role }, ct))
        {
            var user = await _store.FindByEmailAsync(email, ct) ?? throw new InvalidOperationException("user not found after insert");
            var tokens = _issuer.Issue(user);
            return new AuthResult(true, null, null, tokens);
        }

        return new AuthResult(false, AuthError.DuplicateEmail, "Email already registered", null);
    }

    public Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);
        return FindAndValidate(email, request.Password, ct);
    }

    public Task<AuthResult> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var payload = _issuer.ValidateRefreshToken(request.RefreshToken);
        if (payload is null)
            return Task.FromResult(new AuthResult(false, AuthError.InvalidRefreshToken, "Invalid refresh token", null));

        return RotateAsync(payload.UserId, payload.Version, ct);
    }

    public Task LogoutAsync(Guid userId, CancellationToken ct = default)
    {
        _ = _store.BumpTokenVersionAsync(userId, ct);
        return Task.CompletedTask;
    }

    private async Task<AuthResult> FindAndValidate(string email, string password, CancellationToken ct)
    {
        var user = await _store.FindByEmailAsync(email, ct);
        if (user is null || !_hasher.Verify(password, user.PasswordHash))
            return new AuthResult(false, AuthError.InvalidCredentials, "Invalid credentials", null);
        var tokens = _issuer.Issue(user);
        return new AuthResult(true, null, null, tokens);
    }

    private async Task<AuthResult> RotateAsync(Guid userId, int presentedVersion, CancellationToken ct)
    {
        if (!await _store.TryRotateTokenVersionAsync(userId, presentedVersion, ct))
            return new AuthResult(false, AuthError.InvalidRefreshToken, "Invalid refresh token", null);
        var user = await _store.FindByIdAsync(userId, ct) ?? throw new InvalidOperationException("user not found");
        var tokens = _issuer.Issue(user);
        return new AuthResult(true, null, null, tokens);
    }

    private static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
    private static bool IsValidEmail(string email) =>
        email.Contains("@") && email.Split('@')[1].Contains(".");
}
