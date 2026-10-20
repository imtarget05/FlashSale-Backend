using Microsoft.Extensions.Configuration;

namespace OpsBridge.Infrastructure.Persistence;

/// <summary>
/// Single place where the OpsBridge PostgreSQL connection string is resolved.
/// </summary>
/// <remarks>
/// <para>
/// The resolution used to live inline in
/// <see cref="DependencyInjection.AddOpsBridgeInfrastructure"/> and is now shared by
/// the application wiring, the migration entrypoint (<c>--migrate</c>) and the legacy
/// baseline tool (<c>--baseline-legacy</c>). Centralising it is what makes the
/// difference between the two resolution modes expressible at all: the application
/// keeps its historical localhost fallback, while the deployment-time tools must not
/// have one.
/// </para>
/// <para>
/// The fallback is the reason this class exists rather than a one-line helper. A
/// migration entrypoint that silently falls back to <c>localhost</c> would happily
/// migrate the developer's local database and report success while the intended
/// target stays untouched — a data-loss class of bug that is invisible from the exit
/// code alone.
/// </para>
/// <para>
/// OpsBridge has no worker process: <c>src/OpsBridge.Web</c> is a browser front end
/// and <c>src/Order.Worker</c> belongs to the FlashSale context. The only consumers
/// of this resolver are the API and the deployment-time schema tools.
/// </para>
/// </remarks>
public static class OpsBridgeConnectionStringResolver
{
    /// <summary>Configuration section key that holds the OpsBridge-specific connection string.</summary>
    public const string OpsBridgeDbConnectionName = "OpsBridgeDb";

    /// <summary>Configuration section key used as the fallback connection string.</summary>
    public const string DefaultConnectionName = "DefaultConnection";

    /// <summary>Environment variable that maps to <c>ConnectionStrings:OpsBridgeDb</c>.</summary>
    public const string OpsBridgeDbEnvironmentVariable = "ConnectionStrings__OpsBridgeDb";

    /// <summary>Environment variable that maps to <c>ConnectionStrings:DefaultConnection</c>.</summary>
    public const string DefaultConnectionEnvironmentVariable = "ConnectionStrings__DefaultConnection";

    /// <summary>
    /// The historical development fallback, kept byte-for-byte identical to the literal
    /// that used to sit in the DI registration:
    /// <c>"Host=localhost;Port=5432;Database=OpsBridgeDb;Username=postgres;Password=postgres"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The literal is unchanged, the null-versus-blank semantics are not.</b> The
    /// original registration was
    /// <c>configuration.GetConnectionString("OpsBridgeDb") ?? configuration.GetConnectionString("DefaultConnection") ?? LocalhostFallback</c>
    /// — the <c>??</c> operator only falls through on <c>null</c>. A configuration
    /// provider that returns an EMPTY (but not null) value for
    /// <c>ConnectionStrings:OpsBridgeDb</c> therefore produced an empty connection
    /// string, and that empty string was handed to Npgsql.
    /// </para>
    /// <para>
    /// This resolver treats a blank value as "not configured", so such a value now
    /// falls through to <c>ConnectionStrings:DefaultConnection</c> and then to the
    /// localhost fallback. The behaviour therefore DID change, and it changed for the
    /// better: an empty connection string is a configuration mistake that fails later,
    /// far away from its cause, whereas falling through reaches a working connection.
    /// The change is deliberate and is called out here rather than described as
    /// "unchanged".
    /// </para>
    /// </remarks>
    internal const string LocalhostFallback =
        "Host=localhost;Port=5432;Database=OpsBridgeDb;Username=postgres;Password=postgres";

    /// <summary>
    /// Resolve the connection string the application itself uses.
    /// </summary>
    /// <remarks>
    /// Precedence is unchanged from the original inline registration:
    /// <c>ConnectionStrings:OpsBridgeDb</c> → <c>ConnectionStrings:DefaultConnection</c> →
    /// the localhost development fallback. The fallback is intentional here: this is the
    /// path the API and the local test runs take, and removing it would
    /// change existing behaviour for anyone running the stack without a connection string.
    /// The one deliberate difference from the original registration is the treatment of
    /// a BLANK value: see <see cref="LocalhostFallback"/>.
    /// </remarks>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>A connection string. Never <c>null</c> or empty.</returns>
    public static string ResolveForApplication(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return FirstConfigured(configuration)
            ?? LocalhostFallback;
    }

    /// <summary>
    /// Resolve the connection string a deployment-time schema operation must use.
    /// </summary>
    /// <remarks>
    /// Same precedence as <see cref="ResolveForApplication"/>, but the localhost fallback
    /// is deliberately NOT allowed. A migration that quietly targets the wrong database
    /// is indistinguishable from a successful one by exit code alone, so the tool refuses
    /// to start instead of guessing.
    /// </remarks>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>A configured connection string.</returns>
    /// <exception cref="InvalidOperationException">
    /// Neither <c>ConnectionStrings:OpsBridgeDb</c> nor <c>ConnectionStrings:DefaultConnection</c>
    /// is configured (or both are blank).
    /// </exception>
    public static string ResolveForMigration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return FirstConfigured(configuration)
            ?? throw new InvalidOperationException(
                "No OpsBridge connection string is configured. Set either " +
                $"ConnectionStrings:{OpsBridgeDbConnectionName} (environment variable {OpsBridgeDbEnvironmentVariable}) " +
                $"or ConnectionStrings:{DefaultConnectionName} (environment variable {DefaultConnectionEnvironmentVariable}). " +
                "Schema operations deliberately do not fall back to localhost: migrating the wrong database " +
                "would report success while the intended target stays unchanged.");
    }

    /// <summary>
    /// First non-blank connection string in the documented precedence order, or <c>null</c>.
    /// Blank values are treated as "not configured" because an empty
    /// <c>ConnectionStrings__OpsBridgeDb</c> environment variable is a common way to
    /// accidentally shadow the working default.
    /// </summary>
    private static string? FirstConfigured(IConfiguration configuration)
    {
        var opsBridge = configuration.GetConnectionString(OpsBridgeDbConnectionName);
        if (!string.IsNullOrWhiteSpace(opsBridge))
        {
            return opsBridge;
        }

        var fallback = configuration.GetConnectionString(DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return null;
    }
}
