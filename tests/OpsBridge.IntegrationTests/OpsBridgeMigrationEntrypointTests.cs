using OpsBridge.Infrastructure.Persistence;

namespace OpsBridge.IntegrationTests;

/// <summary>
/// The deployment entrypoint contract of ADR-018, exercised at PROCESS level: the real
/// <c>OpsBridge.Api.dll</c> is started with <c>--migrate</c> or <c>--baseline-legacy</c>,
/// and the assertion is on the exit code and on what the deployment log says.
/// </summary>
/// <remarks>
/// This is deliberately not a unit test of <c>Program.cs</c>. The operational contract a
/// deployment pipeline depends on is "run this DLL, get this exit code, read this log" —
/// and that is exactly what is asserted here.
/// </remarks>
[Collection("OpsBridgeIntegration")]
public class OpsBridgeMigrationEntrypointTests(OpsBridgeFixture fixture)
{
    // ------------------------------------------------------------------
    // Row 9 — --migrate without a JWT signing key, in Production
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 9. <c>OpsBridge.Api.dll --migrate</c> with NO <c>Jwt:SigningKey</c> and
    /// <c>ASPNETCORE_ENVIRONMENT=Production</c> exits 0 and creates the schema.
    /// </summary>
    /// <remarks>
    /// This is the proof that the migration step needs a database and nothing else: no
    /// application identity, no JWT key. The JWT validation block would have thrown
    /// <c>"Jwt:SigningKey must be configured in Production."</c> — the assertion on the
    /// output below proves that block was never reached.
    /// </remarks>
    [Fact]
    public async Task MigrateEntrypoint_WithoutJwtSigningKeyInProduction_ExitsZeroAndCreatesSchema()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "entry_migrate");

        var apiDll = MigrationTestHelpers.RequireApiDll();

        var result = await ApiProcessRunner.RunAsync(
            apiDll,
            new[] { "--migrate" },
            MigrationTestHelpers.MigrationEnvironment(database.ConnectionString));

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);

        // The schema was really created.
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));
        var tables = await Db.TableNamesAsync(database.ConnectionString);
        Assert.Contains("Connectors", tables);
        Assert.Contains("IngestedEvents", tables);

        // The JWT gate was never reached.
        Assert.DoesNotContain(
            "Jwt:SigningKey", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);

        // Negative control: the SAME process WITHOUT `--migrate` does fail on the missing
        // JWT key. Without this control the assertion above could pass for the wrong
        // reason (e.g. if the environment variable had been injected by mistake).
        var withoutMigrate = await ApiProcessRunner.RunAsync(
            apiDll,
            Array.Empty<string>(),
            MigrationTestHelpers.MigrationEnvironment(database.ConnectionString));

        Assert.False(withoutMigrate.TimedOut);
        Assert.NotEqual(0, withoutMigrate.ExitCode);
        Assert.Contains(
            "Jwt:SigningKey", withoutMigrate.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Row 10 — --migrate without a connection string
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 10. <c>OpsBridge.Api.dll --migrate</c> with no connection string configured
    /// exits non-zero with a message naming BOTH accepted configuration keys and their
    /// environment variables — and touches no database.
    /// </summary>
    /// <remarks>
    /// "Touches no database" is proved with a reachable target: a real database on the
    /// fixture server whose connection string is present in the child's configuration
    /// under a key the tool does NOT accept. If the tool ever fell back to "whatever
    /// connection string is lying around", it would have migrated that database. It does
    /// not — the database is still empty afterwards.
    /// </remarks>
    [Fact]
    public async Task MigrateEntrypoint_WithoutConnectionString_ExitsNonZeroNamingBothAcceptedKeys()
    {
        await using var reachableButUnaccepted =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "entry_unconfigured");

        var apiDll = MigrationTestHelpers.RequireApiDll();

        var result = await ApiProcessRunner.RunAsync(
            apiDll,
            new[] { "--migrate" },
            new Dictionary<string, string?>
            {
                // Neither accepted key is configured ...
                [MigrationTestHelpers.ConnectionStringEnvironmentVariable] = null,
                [MigrationTestHelpers.DefaultConnectionEnvironmentVariable] = null,
                // ... but a connection string IS present under a key the tool ignores.
                ["ConnectionStrings__SomeOtherDatabase"] = reachableButUnaccepted.ConnectionString,
                ["Jwt__SigningKey"] = null,
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["DOTNET_ENVIRONMENT"] = "Production"
            });

        Assert.False(result.TimedOut);
        Assert.NotEqual(0, result.ExitCode);

        // The message names both accepted configuration keys ...
        Assert.Contains("ConnectionStrings:OpsBridgeDb", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings:DefaultConnection", result.CombinedOutput, StringComparison.Ordinal);

        // ... and both environment variables.
        Assert.Contains("ConnectionStrings__OpsBridgeDb", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__DefaultConnection", result.CombinedOutput, StringComparison.Ordinal);

        // The message states that the localhost fallback is deliberately NOT taken, so
        // the operator knows the tool refused instead of guessing a target.
        Assert.Contains("localhost", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);

        // No database was touched: the reachable database is still empty.
        Assert.Empty(await Db.TableNamesAsync(reachableButUnaccepted.ConnectionString));
    }

    // ------------------------------------------------------------------
    // Row 11 — --baseline-legacy against a drifted legacy database
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 11. <c>OpsBridge.Api.dll --baseline-legacy</c> against a drifted legacy
    /// database exits 2, prints a diagnostic, writes no history row and leaves the data
    /// untouched.
    /// </summary>
    [Fact]
    public async Task BaselineLegacyEntrypoint_WithDriftedLegacyDatabase_ExitsTwoWithoutWritingHistory()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "entry_baseline");

        await MigrationTestHelpers.CreateLegacyDatabaseAsync(database.ConnectionString);
        await MigrationTestHelpers.SeedLegacyRowsAsync(database.ConnectionString);

        // Exactly one drift, so the diagnostic has exactly one thing to name.
        await MigrationTestHelpers.ExecuteAsync(
            database.ConnectionString, "ALTER TABLE \"Connectors\" DROP COLUMN \"BaseUrl\"");

        var apiDll = MigrationTestHelpers.RequireApiDll();

        var result = await ApiProcessRunner.RunAsync(
            apiDll,
            new[] { "--baseline-legacy" },
            MigrationTestHelpers.MigrationEnvironment(database.ConnectionString));

        Assert.False(result.TimedOut);

        // Exit code 2 = drift detected.
        Assert.Equal(2, result.ExitCode);

        // A diagnostic that names the difference.
        Assert.Contains("BaseUrl", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drift", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);

        // No history row was written.
        Assert.False(await Db.TableExistsAsync(database.ConnectionString, Db.HistoryTableName));

        // The data is untouched.
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "Connectors"));
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "IngestedEvents"));
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "AuditEvents"));
    }

    // ------------------------------------------------------------------
    // Row 12 — --baseline-legacy without the CREATEDB privilege
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 12. <c>OpsBridge.Api.dll --baseline-legacy</c> run by an identity WITHOUT the
    /// <c>CREATEDB</c> privilege exits 1, writes nothing to the target, and says what
    /// privilege is missing.
    /// </summary>
    /// <remarks>
    /// The role is created inside the test container for real, so the refusal is
    /// PostgreSQL's (<c>42501 insufficient_privilege</c>) and not a simulated one. The
    /// target database is empty on purpose: the baseline only needs to read the
    /// migration history and the table list before it asks for <c>CREATE DATABASE</c>,
    /// and an empty target keeps the test about the privilege and nothing else.
    /// </remarks>
    [Fact]
    public async Task BaselineLegacyEntrypoint_WithoutCreateDbPrivilege_ExitsOneAndWritesNothing()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "entry_nocreatedb");

        var restrictedConnectionString = await MigrationTestHelpers.CreateRoleWithoutCreateDbAsync(
            fixture.ConnectionString,
            database.DatabaseName,
            "opsbridge_nocreatedb",
            "no-createdb-password");

        var apiDll = MigrationTestHelpers.RequireApiDll();

        var result = await ApiProcessRunner.RunAsync(
            apiDll,
            new[] { "--baseline-legacy" },
            MigrationTestHelpers.MigrationEnvironment(restrictedConnectionString));

        Assert.False(result.TimedOut);

        // Exit code 1 = failure, and NOT the drift code 2: the tool could not verify.
        Assert.Equal(1, result.ExitCode);

        // The message names the refusal and the missing privilege.
        Assert.Contains("CREATE DATABASE", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATEDB", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);

        // Nothing was written to the target: not even the history table.
        Assert.False(await Db.TableExistsAsync(database.ConnectionString, Db.HistoryTableName));
        Assert.Empty(await Db.TableNamesAsync(database.ConnectionString));
    }

    // ------------------------------------------------------------------
    // Row 13 — --migrate against an unreachable database
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 13. <c>OpsBridge.Api.dll --migrate</c> against a host that is not there exits
    /// 1 — the contract code — and does not die with an unhandled exception.
    /// </summary>
    [Fact]
    public async Task MigrateEntrypoint_AgainstUnreachableHost_ExitsOneWithoutStackTrace()
    {
        var apiDll = MigrationTestHelpers.RequireApiDll();

        var environment = MigrationTestHelpers.MigrationEnvironment(
            "Host=127.0.0.1;Port=1;Database=OpsBridgeDb;Username=postgres;Password=postgres");

        var result = await ApiProcessRunner.RunAsync(apiDll, new[] { "--migrate" }, environment);

        Assert.False(result.TimedOut);

        // The contract code, not the 134 an unhandled exception produces.
        Assert.Equal(1, result.ExitCode);

        // A one-line operator message ...
        Assert.Contains("Migration FAILED", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);

        // ... and no stack trace: the failure is a configuration problem, and a stack
        // trace buries the one line that says what to fix.
        Assert.DoesNotContain("   at Npgsql", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }
}
