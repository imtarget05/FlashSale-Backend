using FlashSale.Application.Auth;
using FlashSale.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace FlashSale.Infrastructure.Auth;

/// <summary>
/// JWT minting/validation (ADR-013 §2). Lives in Infrastructure because it
/// depends on a framework package that Application must not reference.
/// </summary>
/// <remarks>
/// Access and refresh tokens are both HS256 JWTs but carry a <c>typ</c> claim,
/// so a refresh token can never be presented as an access token (or vice versa)
/// even though both are signed with the same key.
/// </remarks>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    public const string AccessTokenType = "access";
    public const string RefreshTokenType = "refresh";

    /// <summary>Claim carrying the refresh generation (ADR-013 §3).</summary>
    public const string TokenVersionClaim = "ver";

    /// <summary>Claim distinguishing access from refresh tokens.</summary>
    public const string TokenTypeClaim = "typ";

    public const int AccessTokenMinutes = 15;
    public const int RefreshTokenDays = 7;

    private readonly JwtOptions _options;
    private readonly TimeProvider _clock;

    public JwtTokenIssuer(JwtOptions options, TimeProvider? clock = null)
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    public IssuedTokens Issue(User user)
    {
        var now = _clock.GetUtcNow();
        var access = CreateToken(user, AccessTokenType, now, now.AddMinutes(AccessTokenMinutes));
        var refresh = CreateToken(user, RefreshTokenType, now, now.AddDays(RefreshTokenDays));

        return new IssuedTokens(access, refresh, AccessTokenMinutes * 60);
    }

    public RefreshTokenPayload? ValidateRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;

        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(refreshToken, ValidationParameters(), out _);

            // A refresh token must be a refresh token: without this check an
            // access token would be accepted here and could be rotated forever.
            if (principal.FindFirst(TokenTypeClaim)?.Value != RefreshTokenType) return null;

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(sub, out var userId)) return null;

            if (!int.TryParse(principal.FindFirst(TokenVersionClaim)?.Value, out var version)) return null;

            return new RefreshTokenPayload(userId, version);
        }
        catch (SecurityTokenException)
        {
            // Expired, forged, wrong key, malformed — all indistinguishable to
            // the caller by design (no oracle for attackers).
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string CreateToken(User user, string tokenType, DateTimeOffset issuedAt, DateTimeOffset expires)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(TokenTypeClaim, tokenType),
            new(TokenVersionClaim, user.TokenVersion.ToString()),
            new(ClaimTypes.Role, user.Role),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    };
}