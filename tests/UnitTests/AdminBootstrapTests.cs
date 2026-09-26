using FlashSale.Application.Auth;
using FlashSale.Domain;
using FlashSale.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace FlashSale.UnitTests;

/// <summary>
/// The role-assignment story, end to end at the unit level (ADR-013 §5).
/// <para>
/// Before this, roles were a free-for-all in the worst way: registration always
/// made a CUSTOMER (correct, but unasserted, so it could regress silently), there
/// was NO way to become an ADMIN at all, and the only STAFF account in the system
/// was a seed that re-hashed a hard-coded password on every boot — a permanent
/// backdoor that nobody could rotate.
/// </para>
/// These tests pin the three properties that replace it: self-service cannot
/// choose a role, an operator-held secret is the only path to ADMIN, and a role
/// can never be escalated onto an account somebody else already owns.
/// </summary>
public class AdminBootstrapTests
{
    private const string ValidPassword = "correct-horse-battery";

    private static (AuthService Service, FakeUserStore Store) Build()
    {
        var store = new FakeUserStore();
        var service = new AuthService(
            store,
            new Pbkdf2PasswordHasher(iterations: 1_000),
            new StubTokenIssuer(),
            NullLogger<AuthService>.Instance);
        return (service, store);
    }

    private static BootstrapOptions Options(string? adminToken, string? staffPassword = null) =>
        new() { AdminToken = adminToken, StaffPassword = staffPassword };

    private static ClaimsPrincipal Principal(Guid userId, string role)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)],
            "test");
        return new ClaimsPrincipal(identity);
    }

    // ---------------------------------------------------------------
    // Self-service registration
    // ---------------------------------------------------------------

    [Fact]
    public async Task Register_AlwaysProducesACustomer()
    {
        var (service, _) = Build();

        var result = await service.RegisterAsync(new RegisterRequest("self@example.com", ValidPassword));

        Assert.True(result.Succeeded);
        Assert.Equal(AuthRoles.Customer, result.Tokens!.Role);
    }

    [Fact]
    public void RegisterRequest_HasNoRoleMember_ForTheBodyToBind()
    {
        // Structural, not behavioural: the DTO is what System.Text.Json binds, so
        // as long as it has no Role member, a `{"role":"ADMIN"}` body cannot be
        // bound to anything. A behavioural test would pass today anyway (unknown
        // JSON members are ignored) and would keep passing if the deserializer's
        // settings changed; this one fails the moment someone adds the field.
        var members = typeof(RegisterRequest)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain("Role", members);
    }

    // ---------------------------------------------------------------
    // First-admin bootstrap
    // ---------------------------------------------------------------

    [Fact]
    public async Task BootstrapAdmin_CreatesAnAdminAccount()
    {
        var (service, store) = Build();

        var result = await service.BootstrapAdminAsync(new RegisterRequest("root@example.com", ValidPassword));

        Assert.True(result.Succeeded);
        Assert.Equal(AuthRoles.Admin, result.Tokens!.Role);

        var stored = await store.FindByEmailAsync("root@example.com");
        Assert.Equal(AuthRoles.Admin, stored!.Role);
    }

    [Fact]
    public async Task BootstrapAdmin_RefusesToPromoteAnAlreadyRegisteredAccount()
    {
        // The takeover this prevents: an attacker registers the address the
        // operator is about to bootstrap, and the bootstrap hands it to them.
        var (service, store) = Build();
        await service.RegisterAsync(new RegisterRequest("target@example.com", ValidPassword));

        var result = await service.BootstrapAdminAsync(new RegisterRequest("target@example.com", ValidPassword));

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.DuplicateEmail, result.Error);

        var stored = await store.FindByEmailAsync("target@example.com");
        Assert.Equal(AuthRoles.Customer, stored!.Role); // unchanged
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("")]
    public async Task BootstrapAdmin_AppliesTheSameValidationAsRegister(string email)
    {
        var (service, _) = Build();

        var result = await service.BootstrapAdminAsync(new RegisterRequest(email, ValidPassword));

        Assert.Equal(AuthError.Validation, result.Error);
    }

    [Fact]
    public async Task BootstrapAdmin_RejectsAShortPassword()
    {
        var (service, _) = Build();

        var result = await service.BootstrapAdminAsync(new RegisterRequest("root@example.com", "short"));

        Assert.Equal(AuthError.Validation, result.Error);
    }

    // ---------------------------------------------------------------
    // The secret gate
    // ---------------------------------------------------------------

    [Fact]
    public void BootstrapToken_AcceptsExactlyTheConfiguredSecret()
    {
        var options = Options("s3cret-bootstrap-value");

        Assert.True(options.AcceptsAdminToken("s3cret-bootstrap-value"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("s3cret-bootstrap-valu")]   // one character short
    [InlineData("s3cret-bootstrap-value ")] // one character long
    [InlineData("S3CRET-BOOTSTRAP-VALUE")]   // case differs
    [InlineData("s3cret-bootstrap-value\n")]
    public void BootstrapToken_RejectsAnythingElse(string? presented)
    {
        Assert.False(Options("s3cret-bootstrap-value").AcceptsAdminToken(presented));
    }

    [Fact]
    public void BootstrapToken_IsDisabledEntirelyWhenUnset()
    {
        // The endpoint answers 404 in this state, so the gate must not be
        // satisfiable even by an empty or null header.
        foreach (var token in new[] { null, "", "   ", "anything" })
        {
            var options = Options(adminToken: null);
            Assert.False(options.AdminTokenConfigured);
            Assert.False(options.AcceptsAdminToken(token));
        }
    }

    [Fact]
    public void BootstrapOptions_AreReadFromTheBootstrapConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:AdminToken"] = "from-config",
                ["Bootstrap:StaffPassword"] = "from-config-too",
                ["Bootstrap:StaffEmail"] = "ops@example.com",
            })
            .Build();

        var options = BootstrapOptions.FromConfiguration(configuration);

        Assert.Equal("from-config", options.AdminToken);
        Assert.True(options.AdminTokenConfigured);
        Assert.True(options.StaffPasswordConfigured);
        Assert.True(options.AcceptsAdminToken("from-config"));
        Assert.Equal("ops@example.com", options.StaffEmail);
    }

    [Fact]
    public void StaffPassword_HasNoDefault()
    {
        // The regression that mattered: the seed used to hash a literal that
        // lived in the source file, on every boot. An unset configuration must
        // therefore mean "no seeded account", never "the old password".
        var options = BootstrapOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Null(options.StaffPassword);
        Assert.False(options.StaffPasswordConfigured);
    }

    // ---------------------------------------------------------------
    // Caller identity
    // ---------------------------------------------------------------

    [Fact]
    public void CallerIdentity_ResolvesSubjectAndRole()
    {
        var id = Guid.NewGuid();

        var customer = CallerIdentity.From(Principal(id, AuthRoles.Customer));
        Assert.Equal(id, customer.UserId);
        Assert.False(customer.IsStaffOrAdmin);

        Assert.True(CallerIdentity.From(Principal(id, AuthRoles.Staff)).IsStaffOrAdmin);
        Assert.True(CallerIdentity.From(Principal(id, AuthRoles.Admin)).IsStaffOrAdmin);
    }

    [Fact]
    public void CallerIdentity_WithNoSubject_OwnsNothing()
    {
        // The dangerous default would be `UserId = null` meaning "matches every
        // anonymous order". Here null means "matches no order at all".
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var identity = CallerIdentity.From(anonymous);

        Assert.Null(identity.UserId);
        Assert.False(identity.IsStaffOrAdmin);
    }

    [Fact]
    public void CallerIdentity_Operator_IsTheOnlyNonTokenStaffCaller()
    {
        Assert.True(CallerIdentity.Operator.IsStaffOrAdmin);
        Assert.Null(CallerIdentity.Operator.UserId);
    }
}