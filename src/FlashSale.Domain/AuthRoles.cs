namespace FlashSale.Domain;

/// <summary>
/// Role names as they appear in the JWT <c>role</c> claim and in the
/// <c>Users.Role</c> column (ADR-013 §5).
/// </summary>
/// <remarks>
/// Deliberately a small closed set rather than a CASL-style ability graph: the
/// interview surface needs three roles and two policies, and a speculative
/// permission engine would be more code to defend, not more capability.
/// </remarks>
public static class AuthRoles
{
    public const string Customer = "CUSTOMER";
    public const string Staff = "STAFF";
    public const string Admin = "ADMIN";

    public static readonly string[] All = [Customer, Staff, Admin];

    public static bool IsValid(string? role) =>
        role is not null && Array.IndexOf(All, role) >= 0;
}
