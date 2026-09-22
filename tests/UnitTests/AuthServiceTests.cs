using FlashSale.Application.Auth;
using FlashSale.Domain;
using FlashSale.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests;

/// <summary>
/// Auth use-case tests (ADR-013). No database, no JWT library, no web host —
/// which is the payoff of keeping the use case behind ports.
/// </summary>
public class AuthServiceTests
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

    /// <summary>The stub encodes the user id into the access token.</summary>
    private static Guid UserIdOf(AuthResult result) =>
        Guid.Parse(result.Tokens!.AccessToken.Split(':')[1]);

    [Fact]
    public async Task Register_CreatesCustomer_AndReturnsTokens()
    {
        var (service, _) = Build();

        var result = await service.RegisterAsync(new RegisterRequest("New.User@Example.com", ValidPassword));

        Assert.True(result.Succeeded);
        Assert.Equal("new.user@example.com", result.Tokens!.Email); // normalized
        Assert.Equal(AuthRoles.Customer, result.Tokens.Role);
    }

    [Fact]
    public async Task Register_RejectsDuplicateEmail_CaseInsensitively()
    {
        var (service, _) = Build();
        await service.RegisterAsync(new RegisterRequest("dup@example.com", ValidPassword));

        var second = await service.RegisterAsync(new RegisterRequest("DUP@Example.com", ValidPassword));

        Assert.False(second.Succeeded);
        Assert.Equal(AuthError.DuplicateEmail, second.Error);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("user@nodot")]
    [InlineData("")]
    public async Task Register_RejectsMalformedEmail(string email)
    {
        var (service, _) = Build();

        var result = await service.RegisterAsync(new RegisterRequest(email, ValidPassword));

        Assert.Equal(AuthError.Validation, result.Error);
    }

    [Fact]
    public async Task Register_RejectsShortPassword()
    {
        var (service, _) = Build();

        var result = await service.RegisterAsync(new RegisterRequest("user@example.com", "short"));

        Assert.Equal(AuthError.Validation, result.Error);
    }

    [Fact]
    public async Task Login_SucceedsWithCorrectPassword()
    {
        var (service, _) = Build();
        await service.RegisterAsync(new RegisterRequest("login@example.com", ValidPassword));

        var result = await service.LoginAsync(new LoginRequest("login@example.com", ValidPassword));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveOnEmail()
    {
        var (service, _) = Build();
        await service.RegisterAsync(new RegisterRequest("case@example.com", ValidPassword));

        var result = await service.LoginAsync(new LoginRequest("CASE@Example.COM", ValidPassword));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Login_RejectsWrongPassword_AndUnknownEmail_Identically()
    {
        var (service, _) = Build();
        await service.RegisterAsync(new RegisterRequest("known@example.com", ValidPassword));

        var wrongPassword = await service.LoginAsync(new LoginRequest("known@example.com", "wrong-password-here"));
        var unknownEmail = await service.LoginAsync(new LoginRequest("nobody@example.com", ValidPassword));

        // Same error AND same message: the endpoint must not be an
        // account-enumeration oracle.
        Assert.Equal(AuthError.InvalidCredentials, wrongPassword.Error);
        Assert.Equal(AuthError.InvalidCredentials, unknownEmail.Error);
        Assert.Equal(wrongPassword.Message, unknownEmail.Message);
    }

    [Fact]
    public async Task Refresh_RotatesGeneration_AndIssuesNewPair()
    {
        var (service, store) = Build();
        var registered = await service.RegisterAsync(new RegisterRequest("rotate@example.com", ValidPassword));

        var refreshed = await service.RefreshAsync(new RefreshRequest(registered.Tokens!.RefreshToken));

        Assert.True(refreshed.Succeeded);
        var user = await store.FindByIdAsync(UserIdOf(registered));
        Assert.Equal(1, user!.TokenVersion);
    }

    [Fact]
    public async Task Refresh_RejectsReplayedToken()
    {
        var (service, _) = Build();
        var registered = await service.RegisterAsync(new RegisterRequest("replay@example.com", ValidPassword));

        var first = await service.RefreshAsync(new RefreshRequest(registered.Tokens!.RefreshToken));
        // Same token presented again: its generation is now stale.
        var replay = await service.RefreshAsync(new RefreshRequest(registered.Tokens.RefreshToken));

        Assert.True(first.Succeeded);
        Assert.False(replay.Succeeded);
        Assert.Equal(AuthError.InvalidRefreshToken, replay.Error);
    }

    [Fact]
    public async Task Refresh_ConcurrentRotations_ExactlyOneWins()
    {
        // The race that matters (ADR-013 §3): a read-validate-write rotation
        // would let BOTH of these succeed and make a stolen token replayable.
        var (service, _) = Build();
        var registered = await service.RegisterAsync(new RegisterRequest("race@example.com", ValidPassword));
        var token = registered.Tokens!.RefreshToken;

        var results = await Task.WhenAll(
            service.RefreshAsync(new RefreshRequest(token)),
            service.RefreshAsync(new RefreshRequest(token)));

        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.Equal(1, results.Count(r => !r.Succeeded));
    }

    [Fact]
    public async Task Refresh_RejectsGarbageToken()
    {
        var (service, _) = Build();

        var result = await service.RefreshAsync(new RefreshRequest("not-a-jwt"));

        Assert.Equal(AuthError.InvalidRefreshToken, result.Error);
    }

    [Fact]
    public async Task Logout_InvalidatesRefreshToken()
    {
        var (service, _) = Build();
        var registered = await service.RegisterAsync(new RegisterRequest("logout@example.com", ValidPassword));

        await service.LogoutAsync(UserIdOf(registered));
        var afterLogout = await service.RefreshAsync(new RefreshRequest(registered.Tokens!.RefreshToken));

        Assert.False(afterLogout.Succeeded);
        Assert.Equal(AuthError.InvalidRefreshToken, afterLogout.Error);
    }
}