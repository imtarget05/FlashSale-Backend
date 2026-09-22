using FlashSale.Application.Auth;
using FlashSale.Infrastructure.Auth;

namespace FlashSale.UnitTests;

/// <summary>
/// PBKDF2 hasher tests (ADR-013 §1). These assert the properties that matter:
/// the encoded form is self-describing, verification is exact, and malformed
/// input is rejected rather than throwing.
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    // Low iteration count: these tests assert behaviour, not work factor.
    // Using the production 210k would make the suite needlessly slow.
    private static Pbkdf2PasswordHasher Hasher() => new(iterations: 1_000);

    [Fact]
    public void Hash_ProducesSelfDescribingEncodedString()
    {
        var encoded = Hasher().Hash("correct horse battery staple");
        var parts = encoded.Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("1000", parts[1]);
        Assert.NotEmpty(Convert.FromBase64String(parts[2])); // salt
        Assert.NotEmpty(Convert.FromBase64String(parts[3])); // hash
    }

    [Fact]
    public void Hash_UsesRandomSalt_SoSamePasswordYieldsDifferentHashes()
    {
        var hasher = Hasher();

        var first = hasher.Hash("same-password");
        var second = hasher.Hash("same-password");

        Assert.NotEqual(first, second);
        // ...but both still verify, which is the point of a per-hash salt.
        Assert.True(hasher.Verify("same-password", first));
        Assert.True(hasher.Verify("same-password", second));
    }

    [Fact]
    public void Verify_AcceptsCorrectPassword_AndRejectsWrongOne()
    {
        var hasher = Hasher();
        var encoded = hasher.Hash("s3cret-passphrase");

        Assert.True(hasher.Verify("s3cret-passphrase", encoded));
        Assert.False(hasher.Verify("s3cret-passphras", encoded));
        Assert.False(hasher.Verify("", encoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-encoded-hash")]
    [InlineData("pbkdf2-sha256$1000$onlythreeparts")]
    [InlineData("bcrypt$1000$c2FsdA==$aGFzaA==")]      // unknown scheme
    [InlineData("pbkdf2-sha256$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$!!!notbase64!!!$aGFzaA==")]
    public void Verify_ReturnsFalse_ForMalformedEncodedHash(string malformed)
    {
        // Must not throw: the input may be a row written by an older format.
        Assert.False(Hasher().Verify("any-password", malformed));
    }

    [Fact]
    public void Verify_HonoursIterationCountStoredInTheHash()
    {
        // A hash produced with a different work factor must still verify, which
        // is what allows raising the policy without a flag day.
        var encoded = new Pbkdf2PasswordHasher(iterations: 2_000).Hash("portable");

        Assert.True(new Pbkdf2PasswordHasher(iterations: 1_000).Verify("portable", encoded));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveIterations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pbkdf2PasswordHasher(0));
    }
}