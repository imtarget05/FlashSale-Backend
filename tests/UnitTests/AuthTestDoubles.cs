using FlashSale.Application.Auth;
using FlashSale.Domain;
using FlashSale.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests;

/// <summary>
/// In-memory <see cref="IUserStore"/> that reproduces the ONE behaviour the
/// production store relies on: rotation is a compare-and-swap, so a stale
/// presented version loses. Without that, these tests would pass against a
/// broken implementation.
/// </summary>
internal sealed class FakeUserStore : IUserStore
{
    private readonly Dictionary<Guid, User> _users = [];
    private readonly Lock _gate = new();

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_users.Values.FirstOrDefault(u => u.Email == normalizedEmail));
    }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_users.GetValueOrDefault(id));
    }

    public Task<bool> TryAddAsync(User user, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_users.Values.Any(u => u.Email == user.Email)) return Task.FromResult(false);
            _users[user.Id] = user;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryRotateTokenVersionAsync(Guid userId, int presentedVersion, CancellationToken ct = default)
    {
        lock (_gate)
        {
            // Mirrors: UPDATE ... WHERE Id = @id AND TokenVersion = @presented
            if (!_users.TryGetValue(userId, out var user)) return Task.FromResult(false);
            if (user.TokenVersion != presentedVersion) return Task.FromResult(false);

            user.TokenVersion++;
            return Task.FromResult(true);
        }
    }

    public Task<int> BumpTokenVersionAsync(Guid userId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var user)) return Task.FromResult(0);
            user.TokenVersion++;
            return Task.FromResult(user.TokenVersion);
        }
    }
}

/// <summary>
/// Deterministic stand-in for the real JWT issuer. Encodes the user id and
/// generation into the token text so tests can assert rotation without crypto.
/// </summary>
internal sealed class StubTokenIssuer : ITokenIssuer
{
    public IssuedTokens Issue(User user) =>
        new($"access:{user.Id}:{user.TokenVersion}", $"refresh:{user.Id}:{user.TokenVersion}", 900);

    public RefreshTokenPayload? ValidateRefreshToken(string refreshToken)
    {
        var parts = refreshToken.Split(':');
        if (parts.Length != 3 || parts[0] != "refresh") return null;
        if (!Guid.TryParse(parts[1], out var userId)) return null;
        if (!int.TryParse(parts[2], out var version)) return null;

        return new RefreshTokenPayload(userId, version);
    }
}