namespace FlashSale.Infrastructure.Auth;

using FlashSale.Application.Auth;
using FlashSale.Domain.Entities;
using System.Security.Cryptography;
using System.Text;

public sealed class Pbkdf2PasswordHasher : IPbkdf2PasswordHasher
{
    private readonly int _iterations;
    public Pbkdf2PasswordHasher(int iterations)
    {
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        var hash = DerivePbkdf2(password, salt, _iterations);
        return $"pbkdf2-sha256${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;
        byte[] salt;
        byte[] hash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch { return false; }
        var computed = DerivePbkdf2(password, salt, iterations);
        return CryptographicEquals(computed, hash);
    }

    private static byte[] DerivePbkdf2(string password, byte[] salt, int iterations)
    {
        using var derive = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, iterations);
        return derive.GetBytes(32);
    }

    private static bool CryptographicEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
