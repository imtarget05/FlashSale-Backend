using FlashSale.Domain;
using System.Security.Claims;

namespace FlashSale.Application.Auth;

/// <summary>
/// Who is making an order-scoped call, resolved from the bearer token once at the
/// transport edge and passed down explicitly.
/// </summary>
/// <remarks>
/// It is a required argument on every order-scoped use case rather than an
/// optional one. An "unauthenticated" default would be a standing bypass: any
/// caller that forgot to pass an identity would silently own every anonymous
/// order. Making it mandatory means a new endpoint cannot forget the check.
/// <para>
/// Only <see cref="System.Security.Claims"/> is used, which is BCL — the
/// Application layer stays free of ASP.NET Core (asserted by ArchitectureTests).
/// </para>
/// </remarks>
public sealed record CallerIdentity(Guid? UserId, bool IsStaffOrAdmin)
{
    /// <summary>
    /// A trusted platform caller that is not an end user: an operator runbook, an
    /// automated scan, or a use-case test. Not derivable from a token.
    /// </summary>
    public static CallerIdentity Operator { get; } = new(null, IsStaffOrAdmin: true);

    /// <summary>
    /// Resolve from a validated principal. A missing or unparseable <c>sub</c>
    /// yields <c>UserId = null</c>, which owns nothing — never everything.
    /// </summary>
    public static CallerIdentity From(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        // FindFirst(..)?.Value rather than the FindFirstValue extension: the
        // extension ships with ASP.NET Core, and Application must not reference it.
        var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        var userId = Guid.TryParse(sub, out var parsed) ? parsed : (Guid?)null;

        var isStaffOrAdmin = user.IsInRole(AuthRoles.Staff) || user.IsInRole(AuthRoles.Admin);
        return new CallerIdentity(userId, isStaffOrAdmin);
    }
}