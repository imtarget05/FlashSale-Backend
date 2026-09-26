using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace FlashSale.Infrastructure.Auth;

/// <summary>
/// Operator-facing bootstrap configuration (bound from the <c>Bootstrap</c>
/// section). Two independent jobs, both opt-in:
/// <list type="bullet">
///   <item><description><see cref="AdminToken"/> enables <c>POST /api/auth/bootstrap</c>,
///   the only way an ADMIN account can ever come into existence. Unset ⇒ the
///   endpoint is disabled (404), so a deployment that never configures it has no
///   admin-creation path at all.</description></item>
///   <item><description><see cref="StaffPassword"/> is the password for the seeded
///   STAFF account. It is read from configuration and has NO default, because the
///   previous seed re-hashed a hard-coded literal on every boot — a permanent
///   backdoor that could not be rotated without a code change.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Lives in Infrastructure (not the API project) so the seeder and the endpoint
/// gate share one definition, and so it is reachable from the unit test project
/// without taking a dependency on the web host.
/// </remarks>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    /// <summary>Secret that authorises the first-admin bootstrap. No default.</summary>
    public string? AdminToken { get; set; }

    /// <summary>Account the STAFF seed creates. Not a secret, so it has a default.</summary>
    public string StaffEmail { get; set; } = "staff@flashsale.local";

    /// <summary>Password for <see cref="StaffEmail"/>. No default, by design.</summary>
    public string? StaffPassword { get; set; }

    public static BootstrapOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new BootstrapOptions();
        configuration.GetSection(SectionName).Bind(options);
        return options;
    }

    /// <summary>True when an operator has configured a bootstrap secret.</summary>
    public bool AdminTokenConfigured => !string.IsNullOrWhiteSpace(AdminToken);

    /// <summary>True when the STAFF seed has an explicitly configured password.</summary>
    public bool StaffPasswordConfigured => !string.IsNullOrWhiteSpace(StaffPassword);

    /// <summary>
    /// Constant-time comparison of a presented secret against
    /// <see cref="AdminToken"/>. Returns false whenever the endpoint is disabled,
    /// so an unset token can never authenticate.
    /// </summary>
    /// <remarks>
    /// Both sides are hashed to a fixed 32 bytes first. <c>FixedTimeEquals</c>
    /// short-circuits on a length mismatch, so comparing raw UTF-8 bytes would leak
    /// the secret's LENGTH through timing; hashing first removes that.
    /// </remarks>
    public bool AcceptsAdminToken(string? presented)
    {
        var expected = AdminToken;
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrEmpty(presented))
            return false;

        Span<byte> presentedHash = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> expectedHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(presented), presentedHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(expected), expectedHash);

        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }
}