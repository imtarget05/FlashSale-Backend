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

