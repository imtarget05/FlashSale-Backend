using Microsoft.Extensions.Configuration;

namespace FlashSale.Infrastructure.Auth;

/// <summary>Bound from the <c>Auth:Jwt</c> configuration section (ADR-013 §2).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    /// <summary>Minimum key length in bytes; HS256 keys shorter than this are weak.</summary>
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "FlashSale";
    public string Audience { get; set; } = "FlashSale";
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Resolve the signing key, failing fast in Production when it is missing or
    /// too short. A development fallback keeps `docker compose` working without
    /// a secret, but it must never be usable in Production.
    /// </summary>
    public static JwtOptions FromConfiguration(IConfiguration configuration, bool isProduction)
    {
        var options = new JwtOptions();
        configuration.GetSection(SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            if (isProduction)
                throw new InvalidOperationException(
                    $"{SectionName}:SigningKey is required in Production. " +
                    "Set it from a secret store; do not ship a default key.");

            options.SigningKey = "dev-only-insecure-signing-key-change-me-32b+";
        }

        if (System.Text.Encoding.UTF8.GetByteCount(options.SigningKey) < MinimumKeyBytes)
            throw new InvalidOperationException(
                $"{SectionName}:SigningKey must be at least {MinimumKeyBytes} bytes for HS256.");

        return options;
    }
}