using FlashSale.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Order.Api.Auth;

/// <summary>
/// Authorization policies (ADR-013 §5). Two policies cover the whole interview
/// surface; a CASL-style ability graph would be more code to defend, not more
/// capability.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Product writes: STAFF or ADMIN.</summary>
    public const string StaffOrAdmin = "StaffOrAdmin";

    /// <summary>Any authenticated user.</summary>
    public const string Authenticated = "Authenticated";

    public static void AddFlashSalePolicies(this AuthorizationBuilder builder)
    {
        builder.AddPolicy(StaffOrAdmin, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(AuthRoles.Staff, AuthRoles.Admin));

        builder.AddPolicy(Authenticated, policy => policy.RequireAuthenticatedUser());
    }
}