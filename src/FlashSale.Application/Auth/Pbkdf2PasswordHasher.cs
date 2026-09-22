using System.Security.Cryptography;

namespace FlashSale.Application.Auth;

/// <summary>
/// Port: password hashing/verification (ADR-013 §1). Kept behind an interface so
/// the use case is testable without touching crypto parameters.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hash a plaintext password into a self-describing encoded string.</summary>
    string Hash(string password);

    /// <summary>
    /// Constant-time verification against an encoded hash. Returns false for
    /// malformed input rather than throwing, because the input may be a row
    /// written by an older/unknown format.
    /// </summary>
    bool Verify(string password, string encodedHash);
}

/// <summary>
/// PBKDF2-HMAC-SHA256 hasher. Uses the .NET 10 one-shot static
/// <see cref="Rfc2898DeriveBytes.Pbkdf2(string, byte[], int, HashAlgorithmName, int)"/>
/// API — the <c>Rfc2898DeriveBytes</c> constructors are obsolete.
/// </summary>
/// <remarks>
/// The encoded form is <c>pbkdf2-sha256$iterations$salt$hash</c>, so the
/// iteration count travels with the hash. That is what allows a future policy
/// increase to rehash on next login instead of forcing a flag day.
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>OWASP-aligned work factor for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 210_000;

    private const int SaltBytes = 32;
    private const int HashBytes = 32;
    private const string Scheme = "pbkdf2-sha256";

    private readonly int _iterations;

    public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iteration count must be positive.");

        _iterations = iterations;
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, _iterations, HashAlgorithmName.SHA256, HashBytes);

        return string.Join('$', Scheme, _iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string encodedHash)
    {
        if (password is null || string.IsNullOrEmpty(encodedHash)) return false;

        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0) return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Constant-time compare: a length/shape error must not leak usable timing.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}