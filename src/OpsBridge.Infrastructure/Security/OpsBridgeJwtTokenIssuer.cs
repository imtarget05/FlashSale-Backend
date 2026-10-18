using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace OpsBridge.Infrastructure.Security;

public interface IOpsBridgeTokenIssuer
{
    string IssueToken(string userId, string username, string role);
}

public sealed class OpsBridgeJwtTokenIssuer : IOpsBridgeTokenIssuer
{
    private readonly SymmetricSecurityKey _key;
    private readonly string _issuer;
    private readonly string _audience;

    public const string DefaultSigningKey = "OpsBridge-Secret-Key-Must-Be-At-Least-32-Bytes-Long-For-HMAC-SHA256!";
    public const string DefaultIssuer = "OpsBridge.Api";
    public const string DefaultAudience = "OpsBridge.Web";

    public OpsBridgeJwtTokenIssuer(string? signingKey = null, string? issuer = null, string? audience = null)
    {
        _issuer = issuer ?? DefaultIssuer;
        _audience = audience ?? DefaultAudience;
        var keyBytes = Encoding.UTF8.GetBytes(signingKey ?? DefaultSigningKey);
        _key = new SymmetricSecurityKey(keyBytes);
    }

    public string IssueToken(string userId, string username, string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Name, username),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
