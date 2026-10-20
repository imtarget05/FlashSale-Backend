using Microsoft.EntityFrameworkCore;
using OpsBridge.Infrastructure.Persistence;

namespace OpsBridge.IntegrationTests;

/// <summary>
/// The production-grade migration lifecycle matrix (ADR-018).
/// </summary>
/// <remarks>
/// <para>
/// Every test in this class owns a uniquely named throw-away database on the fixture's
/// PostgreSQL server, because the matrix is about the STATE a database starts in:
/// empty, already migrated, legacy, drifted, contended, poisoned. The shared
/// <c>OpsBridgeTestDb</c> is never truncated and never dropped.
/// </para>
/// <para>
/// Style: plain xUnit <c>Assert.*</c> — this repository has no assertion library and no
/// mocking library, so the only fake used is the hand-written
/// <see cref="RecordingLogger"/> in <c>MigrationTestSupport.cs</c>.
/// </para>
/// </remarks>
[Collection("OpsBridgeIntegration")]
public class OpsBridgeMigrationLifecycleTests(OpsBridgeFixture fixture)
{
    // ------------------------------------------------------------------
    // Row 1 — fresh PostgreSQL
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 1. A fresh, empty PostgreSQL database is migrated from scratch: the schema is
    /// created and the migration is recorded in EF's history table.
    /// </summary>
    [Fact]
    public async Task FreshPostgreSQL_MigrationCreatesTheSchemaSuccessfully()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "mig_fresh");
        var logger = new RecordingLogger();

        var outcome = await OpsBridgeMigrationRunner.RunAsync(database.ConnectionString, logger);

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, outcome.ExitCode);
        Assert.Null(outcome.Failure);
        Assert.False(outcome.AlreadyAtHead);
        Assert.NotEmpty(outcome.PendingMigrations);
        Assert.NotEmpty(outcome.AppliedMigrations);
        Assert.NotEmpty(outcome.NewlyRecordedMigrations);
        Assert.NotEmpty(outcome.RecordedMigrations);

        var tables = await Db.TableNamesAsync(database.ConnectionString);
        Assert.Contains("Connectors", tables);
        Assert.Contains("IngestedEvents", tables);
        Assert.Contains("AuditEvents", tables);
        Assert.Contains("InboxMessages", tables);
        Assert.Contains(Db.HistoryTableName, tables);

        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));
    }

    // ------------------------------------------------------------------
    // Row 2 — existing migrated database
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 2. Running the migration against a database that is already at head applies
    /// nothing and reports success: a re-run must never fail a deployment pipeline.
    /// </summary>
    [Fact]
    public async Task ExistingMigratedDatabase_MigrationIsNotReapplied()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "mig_existing");

        var first = await OpsBridgeMigrationRunner.RunAsync(
            database.ConnectionString, new RecordingLogger());

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, first.ExitCode);
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));

        var secondLogger = new RecordingLogger();
        var second = await OpsBridgeMigrationRunner.RunAsync(database.ConnectionString, secondLogger);

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, second.ExitCode);
        Assert.Null(second.Failure);
        Assert.True(second.AlreadyAtHead);
        Assert.Empty(second.AppliedMigrations);
        Assert.Empty(second.NewlyRecordedMigrations);
        Assert.Empty(second.PendingMigrations);
        Assert.Contains("already at head", secondLogger.AllText, StringComparison.OrdinalIgnoreCase);

        // Nothing was re-applied: the history table still holds exactly one row.
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));
    }

    // ------------------------------------------------------------------
    // Row 3 — legacy EnsureCreated database
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 3. A legacy database created with <c>EnsureCreatedAsync()</c> — the old code
    /// path, which creates the schema but no migration history — is adopted safely, and
    /// the business data survives the adoption untouched.
    /// </summary>
    [Fact]
    public async Task LegacyEnsureCreatedDatabase_IsAdoptedSafelyAndDataIsPreserved()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "mig_legacy");

        await MigrationTestHelpers.CreateLegacyDatabaseAsync(database.ConnectionString);
        await MigrationTestHelpers.SeedLegacyRowsAsync(database.ConnectionString);

        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "Connectors"));
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "IngestedEvents"));
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "AuditEvents"));

        // The legacy database has a schema but no migration history — that is the exact
        // state the baseline tool exists for.
        Assert.False(await Db.TableExistsAsync(database.ConnectionString, Db.HistoryTableName));

        var logger = new RecordingLogger();
        var outcome = await OpsBridgeLegacyBaseline.AdoptAsync(database.ConnectionString, logger);

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, outcome.ExitCode);
        Assert.Null(outcome.Failure);
        Assert.True(outcome.HistoryWritten);
        Assert.True(outcome.VerificationRan);
        Assert.False(outcome.DriftDetected);
        Assert.False(outcome.AlreadyManaged);
        Assert.Empty(outcome.Differences);

        // The history row now exists, so EF considers the database managed.
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));

        // The data is still there, unchanged.
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "Connectors"));
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "IngestedEvents"));
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "AuditEvents"));

        Assert.Equal(
            outcome.RowCountsBefore.OrderBy(count => count.Table).ToList(),
            outcome.RowCountsAfter!.OrderBy(count => count.Table).ToList());

        // The adoption produced a state EF agrees with: a migration run is now a no-op.
        var migration = await OpsBridgeMigrationRunner.RunAsync(
            database.ConnectionString, new RecordingLogger());

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, migration.ExitCode);
        Assert.True(migration.AlreadyAtHead);
        Assert.Empty(migration.PendingMigrations);
    }

    // ------------------------------------------------------------------
    // Row 4 — schema mismatch
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 4. A legacy database whose schema has drifted is refused: the baseline stops,
    /// reports the difference by name, and writes nothing.
    /// </summary>
    [Fact]
    public async Task SchemaMismatch_BaselineStopsSafelyWithADiagnostic()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "mig_drift");

        await MigrationTestHelpers.CreateLegacyDatabaseAsync(database.ConnectionString);
        await MigrationTestHelpers.SeedLegacyRowsAsync(database.ConnectionString);

        // Exactly one drift: the "BaseUrl" column disappears.
        await MigrationTestHelpers.ExecuteAsync(
            database.ConnectionString, "ALTER TABLE \"Connectors\" DROP COLUMN \"BaseUrl\"");

        var logger = new RecordingLogger();
        var outcome = await OpsBridgeLegacyBaseline.AdoptAsync(database.ConnectionString, logger);

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeDriftDetected, outcome.ExitCode);
        Assert.True(outcome.DriftDetected);
        Assert.False(outcome.HistoryWritten);
        Assert.NotNull(outcome.Failure);
        Assert.NotEmpty(outcome.Differences);

        // The diagnostic names the difference, not just "something is wrong".
        Assert.Contains(
            outcome.Differences,
            difference => difference.Contains("BaseUrl", StringComparison.OrdinalIgnoreCase));

        // The same diagnostic reached the log an operator would read.
        Assert.Contains("BaseUrl", logger.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drift", logger.AllText, StringComparison.OrdinalIgnoreCase);

        // Nothing was written: no history table at all, and the data is intact.
        Assert.False(await Db.TableExistsAsync(database.ConnectionString, Db.HistoryTableName));
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "Connectors"));
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "IngestedEvents"));
        Assert.Equal(1, await Db.CountRowsAsync(database.ConnectionString, "AuditEvents"));
    }

    // ------------------------------------------------------------------
    // Row 5 — 2–5 instances starting together
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 5. Five instances start at the same instant against one fresh database. This
    /// is the empirical proof that EF Core 10's built-in migration lock is sufficient:
    /// every run must succeed, exactly one history row may exist, and no run may fail
    /// with <c>42P07 duplicate_table</c> or a lock timeout.
    /// </summary>
    /// <remarks>
    /// The assertion is deliberately strict about ATTRIBUTION: exactly one instance may
    /// report that it applied a migration, and the other four must report that they
    /// found the schema at head. The weaker "the union of the applied sets has one
    /// distinct id" cannot detect a runner that claims a migration it did not apply.
    /// </remarks>
    [Fact]
    public async Task ConcurrentInstancesStartingTogether_MigrationHasNoRaceCondition()
    {
        const int instanceCount = 5;

        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "mig_race");

        // A gate so all five runs really do start together rather than one after another.
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new RecordingLogger();

        var runs = Enumerable.Range(0, instanceCount)
            .Select(async _ =>
            {
                await startGate.Task;
                return await OpsBridgeMigrationRunner.RunAsync(database.ConnectionString, logger);
            })
            .ToArray();

        startGate.SetResult();
        var outcomes = await Task.WhenAll(runs);

        Assert.Equal(instanceCount, outcomes.Length);

        foreach (var outcome in outcomes)
        {
            Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, outcome.ExitCode);
            Assert.Null(outcome.Failure);
        }

        // Exactly one migration was recorded — the losers found the schema at head.
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));

        // Exactly ONE instance did the work.
        var applied = outcomes.Where(outcome => !outcome.AlreadyAtHead).ToList();
        Assert.Single(applied);
        Assert.NotEmpty(applied[0].AppliedMigrations);
        Assert.NotEmpty(applied[0].NewlyRecordedMigrations);

        // The other four found the schema at head and applied nothing themselves.
        var atHead = outcomes.Where(outcome => outcome.AlreadyAtHead).ToList();
        Assert.Equal(instanceCount - 1, atHead.Count);
        foreach (var outcome in atHead)
        {
            Assert.Empty(outcome.AppliedMigrations);
        }

        // The applied migration set across all five runs is the single migration of this
        // repository — nothing was applied twice, nothing unexpected was applied.
        var appliedMigrations = outcomes
            .SelectMany(outcome => outcome.AppliedMigrations)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Single(appliedMigrations);

        // The log must not claim a process applied a DDL it did not apply: exactly one
        // "This run applied 1 migration(s)" line, no matter how many instances ran.
        Assert.Equal(1, CountOccurrences(logger.AllText, "This run applied 1 migration(s)"));

        // No duplicate-table error, no lock timeout, no cancellation anywhere in the run.
        Assert.DoesNotContain("42P07", logger.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicate_table", logger.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TimeoutException", logger.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lock timeout", logger.AllText, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Row 6 — migration failing part-way
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 6. A migration that fails is not recorded as complete, and it leaves no
    /// half-applied domain tables behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Honest scope of this assertion.</b> EF Core 10 commits <b>per migration</b>: the
    /// history INSERT for a migration lives in the same transaction as that migration's
    /// DDL. This test therefore proves <i>per-migration</i> atomicity — a failed
    /// migration leaves no partial schema and no history row. It does NOT prove
    /// all-or-nothing atomicity across SEVERAL migrations, because this repository has
    /// exactly one migration, so there is no multi-migration sequence to interrupt. That
    /// limit is a property of EF Core's migration semantics, not a defect in
    /// <see cref="OpsBridgeMigrationRunner"/>: a runner cannot make EF atomic across
    /// migrations without disabling EF's own per-migration transaction, which Microsoft
    /// does not support.
    /// </para>
    /// <para>
    /// The failure is provoked by pre-creating a conflicting <c>"Connectors"</c> table, so
    /// the migration's own <c>CREATE TABLE "Connectors"</c> fails with
    /// <c>42P07 duplicate_table</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MigrationFailingPartWay_IsNotRecordedAsComplete()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "mig_fail");

        // Poison the target: a "Connectors" table with a completely different shape.
        await MigrationTestHelpers.ExecuteAsync(
            database.ConnectionString,
            """
            CREATE TABLE "Connectors" (
                "Id" uuid NOT NULL,
                CONSTRAINT "PK_Connectors" PRIMARY KEY ("Id"))
            """);

        var logger = new RecordingLogger();
        var outcome = await OpsBridgeMigrationRunner.RunAsync(database.ConnectionString, logger);

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeFailure, outcome.ExitCode);
        Assert.NotNull(outcome.Failure);
        Assert.Empty(outcome.AppliedMigrations);
        Assert.Empty(outcome.RecordedMigrations);
        Assert.Contains("Connectors", outcome.Failure!, StringComparison.OrdinalIgnoreCase);

        // The failed migration was NOT recorded as complete: either the history table
        // was never created, or it exists and is empty.
        var historyTableExists = await Db.TableExistsAsync(database.ConnectionString, Db.HistoryTableName);
        if (historyTableExists)
        {
            Assert.Equal(0, await Db.HistoryRowCountAsync(database.ConnectionString));
        }

        // No half-applied domain tables. The migration creates "AuditEvents" BEFORE
        // "Connectors", so a rollback must have removed it again — otherwise the failed
        // migration would have left the database in a mixed state.
        var tables = await Db.TableNamesAsync(database.ConnectionString);
        Assert.DoesNotContain("AuditEvents", tables);
        Assert.DoesNotContain("IngestedEvents", tables);
        Assert.DoesNotContain("InboxMessages", tables);

        // The pre-existing conflicting table was left exactly as it was.
        Assert.Contains("Connectors", tables);
        Assert.Equal(0, await Db.CountRowsAsync(database.ConnectionString, "Connectors"));
    }

    // ------------------------------------------------------------------
    // Row 7 — restart after migration
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 7. Restarting after a migration is idempotent — at runner level and at
    /// process level.
    /// </summary>
    [Fact]
    public async Task RestartAfterMigration_IsIdempotentAtRunnerAndProcessLevel()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "mig_restart");

        // ---- runner level: everything is disposed between the two runs ----
        var first = await OpsBridgeMigrationRunner.RunAsync(
            database.ConnectionString, new RecordingLogger());

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, first.ExitCode);
        Assert.False(first.AlreadyAtHead);
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));

        var secondLogger = new RecordingLogger();
        var second = await OpsBridgeMigrationRunner.RunAsync(database.ConnectionString, secondLogger);

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, second.ExitCode);
        Assert.True(second.AlreadyAtHead);
        Assert.Empty(second.AppliedMigrations);
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));

        // ---- process level: the real deployment entrypoint, run twice ----
        var apiDll = MigrationTestHelpers.RequireApiDll();

        var firstProcess = await ApiProcessRunner.RunAsync(
            apiDll,
            new[] { "--migrate" },
            MigrationTestHelpers.MigrationEnvironment(database.ConnectionString));

        Assert.False(firstProcess.TimedOut);
        Assert.Equal(0, firstProcess.ExitCode);
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));

        // The second process finds the schema at head and changes nothing.
        var secondProcess = await ApiProcessRunner.RunAsync(
            apiDll,
            new[] { "--migrate" },
            MigrationTestHelpers.MigrationEnvironment(database.ConnectionString));

        Assert.False(secondProcess.TimedOut);
        Assert.Equal(0, secondProcess.ExitCode);
        Assert.Contains(
            "already at head", secondProcess.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));
    }

    // ------------------------------------------------------------------
    // Row 8 — the baseline against an ALREADY MANAGED database
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 8. A database that already carries migration history is not simply blessed:
    /// the baseline verifies it against a scratch database migrated up to the same
    /// migration, writes nothing, and reports that the verification ran.
    /// </summary>
    [Fact]
    public async Task AlreadyManagedDatabase_IsVerifiedAndNothingIsWritten()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "mig_managed");

        // A genuinely migrated database: the history holds the migration id EF wrote.
        var migration = await OpsBridgeMigrationRunner.RunAsync(
            database.ConnectionString, new RecordingLogger());

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, migration.ExitCode);
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));

        var logger = new RecordingLogger();
        var outcome = await OpsBridgeLegacyBaseline.AdoptAsync(database.ConnectionString, logger);

        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeSuccess, outcome.ExitCode);
        Assert.Null(outcome.Failure);
        Assert.True(outcome.AlreadyManaged);
        Assert.False(outcome.HistoryWritten);
        Assert.False(outcome.DriftDetected);

        // The verification ACTUALLY ran — an unverified "success" is not a success.
        Assert.True(outcome.VerificationRan);
        Assert.Empty(outcome.Differences);

        // The database is at head, so nothing is pending for the --migrate path.
        Assert.Empty(outcome.PendingMigrations);

        // Nothing was written: still exactly one history row.
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));

        // And the log proves the verification ran.
        Assert.Contains("Verification passed", logger.AllText, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Row 9 — an unknown recorded migration id
    // ------------------------------------------------------------------

    /// <summary>
    /// Row 9. A history table that records a migration id THIS BUILD DOES NOT KNOW is
    /// unknown state: the baseline refuses it with exit code 2 instead of blessing it
    /// with exit code 0.
    /// </summary>
    [Fact]
    public async Task UnknownRecordedMigrationId_IsRefusedWithDriftExitCode()
    {
        await using var database =
            await MigrationTestDatabase.CreateAsync(fixture.ConnectionString, "mig_unknown");

        await MigrationTestHelpers.CreateLegacyDatabaseAsync(database.ConnectionString);

        await MigrationTestHelpers.ExecuteAsync(
            database.ConnectionString,
            """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId"))
            """);

        await MigrationTestHelpers.ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('20200101000000_AMigrationFromAnotherBuild', '9.9.9')
            """);

        var logger = new RecordingLogger();
        var outcome = await OpsBridgeLegacyBaseline.AdoptAsync(database.ConnectionString, logger);

        // Exit code 2: the state is unknown, so the database is refused.
        Assert.Equal(OpsBridgeMigrationRunner.ExitCodeDriftDetected, outcome.ExitCode);
        Assert.True(outcome.DriftDetected);
        Assert.True(outcome.AlreadyManaged);
        Assert.False(outcome.HistoryWritten);
        Assert.False(outcome.VerificationRan);
        Assert.NotNull(outcome.Failure);

        // The diagnostic names the unknown id.
        Assert.Contains(
            outcome.Differences,
            difference => difference.Contains("20200101000000_AMigrationFromAnotherBuild", StringComparison.Ordinal));
        Assert.Contains(
            "20200101000000_AMigrationFromAnotherBuild", logger.AllText, StringComparison.Ordinal);

        // Nothing was written: the history table still holds exactly the one row.
        Assert.Equal(1, await Db.HistoryRowCountAsync(database.ConnectionString));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Number of times a fragment occurs in a text. Used to prove that the log makes
    /// exactly one "this run applied a migration" claim.
    /// </summary>
    private static int CountOccurrences(string text, string fragment)
    {
        var count = 0;
        var index = text.IndexOf(fragment, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
