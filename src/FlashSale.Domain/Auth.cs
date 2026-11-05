namespace FlashSale.Domain;

public enum AuthRoles
{
    Customer,
    Staff,
    Admin
}

public sealed class CallerIdentity
{
    public Guid? UserId { get; private set; }
    public string? Role { get; private set; }
    public bool IsStaffOrAdmin => Role is nameof(AuthRoles.Staff) or nameof(AuthRoles.Admin);

    public static CallerIdentity Operator { get; } = new();

    private CallerIdentity() { }

    public static CallerIdentity From(System.Security.Claims.ClaimsPrincipal principal)
    {
        var identity = new CallerIdentity();
        if (principal == null || principal.Identity == null || !principal.Identity.IsAuthenticated)
            return identity;
        var sub = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(sub, out var id)) identity.UserId = id;
        identity.Role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        return identity;
    }
}
