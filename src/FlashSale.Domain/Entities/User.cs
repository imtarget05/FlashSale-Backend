namespace FlashSale.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = nameof(AuthRoles.Customer);
    public int TokenVersion { get; set; }
}
