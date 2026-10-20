using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace OpsBridge.Infrastructure.Persistence;

/// <summary>Row count of one user table, captured before and after a baseline write.</summary>
/// <param name="Table">Table name.</param>
/// <param name="Rows">Number of rows in the table.</param>
public sealed record TableRowCount(string Table, long Rows);

/// <summary>
/// Result of one legacy-baseline adoption attempt.
/// </summary>
/// <param name="ExitCode">
/// <see cref="OpsBridgeMigrationRunner.ExitCodeSuccess"/> (0) on success or when the
/// database was already managed AND the verification passed,
/// <see cref="OpsBridgeMigrationRunner.ExitCodeFailure"/> (1) on a hard failure,
/// <see cref="OpsBridgeMigrationRunner.ExitCodeDriftDetected"/> (2) when drift — or an
/// unknown migration state — was detected and nothing was written.
/// </param>
/// <param name="AlreadyManaged">True when the target already had migration history rows.</param>
/// <param name="DriftDetected">True when the schema comparison found differences.</param>
/// <param name="HistoryWritten">True when the history table and baseline row were written.</param>
/// <param name="VerificationRan">
/// True when the schema comparison ACTUALLY ran. A "successful" outcome without a
/// verification is not a successful baseline, so the outcome says which it was.
/// </param>
/// <param name="Differences">Human-readable list of schema differences (empty on success).</param>
/// <param name="PendingMigrations">
/// Migrations that remain pending for the normal <c>--migrate</c> path. Only the
/// "already managed" path reports them: it knows the recorded history and the compiled
/// migration set, so it knows what is left to apply.
/// </param>
/// <param name="RowCountsBefore">Per-table row counts captured before the write.</param>
/// <param name="RowCountsAfter">
/// Per-table row counts captured after the write, or <c>null</c> when they were NOT
/// measured. A failure path reports <c>null</c> rather than repeating the pre-write
/// counts: an unmeasured value must not be presented as a measurement.
/// </param>
/// <param name="Failure">Failure description, or <c>null</c> on success.</param>
public sealed record BaselineOutcome(
    int ExitCode,
    bool AlreadyManaged,
    bool DriftDetected,
    bool HistoryWritten,
    bool VerificationRan,
    IReadOnlyList<string> Differences,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<TableRowCount> RowCountsBefore,
    IReadOnlyList<TableRowCount>? RowCountsAfter,
    string? Failure);

/// <summary>
/// Adopts a legacy (<c>EnsureCreated</c>-shaped) OpsBridge database into EF Core's
/// migration history — but only after proving that its schema is identical to the one
/// the migration produces.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem.</b> <c>EnsureCreatedAsync()</c> creates the schema without creating
/// <c>__EFMigrationsHistory</c>. A later <c>MigrateAsync()</c> therefore believes nothing
/// has been applied and tries to <c>CREATE TABLE</c> everything, which PostgreSQL
/// rejects with <c>42P07 duplicate_table</c>. Microsoft documents that
/// <c>EnsureCreated</c> "can not be used with migrations"; there is no built-in EF
/// command that adopts such a database.
/// </para>
/// <para>
/// <b>Why the baseline is verified, not blind.</b> Writing a history row without
/// comparing schemas would tell EF "the schema is at head" even when it is not — and
/// every later migration would then be applied on top of an unknown starting point.
/// So this tool builds a scratch database on the same server, applies the real
/// migration to it, and compares the two schemas. Only a zero-difference comparison
/// authorises the history write.
/// </para>
/// <para>
/// <b>What the comparison covers.</b> Tables, columns (name, type, nullability,
/// default), primary keys, unique constraints, indexes and foreign keys — in the
/// <c>public</c> schema only. Views, sequences, enums, functions, triggers and every
/// other schema are NOT compared: they are not owned by EF's migrations, so a
/// difference in them is not evidence of drift.
/// </para>
/// <para>
/// <b>The write is atomic.</b> Creating the history table, inserting the baseline rows
/// and the post-write verification happen inside ONE explicit transaction. The
/// transaction is committed only when the verification reports zero differences;
/// otherwise it is rolled back, so a target can never be left carrying history rows
/// for a schema that was not verified. (This is direct SQL, not <c>MigrateAsync</c>;
/// EF's prohibition on wrapping <c>MigrateAsync</c> in a user transaction does not
/// apply.)
/// </para>
/// <para>
/// <b>"Already managed" is verified too.</b> A non-empty history table is not proof of
/// a healthy database: the recorded migration ids are checked against the migrations
/// THIS build compiled, and the target's schema is compared against a scratch database
/// migrated up to the same migration. An id this build does not know is unknown state,
/// and unknown state is refused (exit 2), not blessed.
/// </para>
/// <para>
/// <b>Never modifies business data.</b> The statements executed against the target are
/// <c>CREATE TABLE "__EFMigrationsHistory"</c>, one <c>INSERT</c> per baseline row, and
/// the read-only <c>SELECT COUNT(*)</c> calls that produce the row-count fingerprint.
/// Row counts are captured before and after to prove that nothing else changed.
/// </para>
/// <para>
/// <b>Requires CREATE DATABASE.</b> The scratch database is the only way to obtain an
/// authoritative "what does the migration produce" schema without guessing. If the
/// identity cannot create databases the tool aborts without writing anything rather
/// than falling back to an unverified baseline.
/// </para>
/// </remarks>
public static class OpsBridgeLegacyBaseline
{
    /// <summary>Name of EF Core's migration history table.</summary>
    public const string HistoryTableName = "__EFMigrationsHistory";

    /// <summary>
    /// Adopt the target database into EF Core's migration history, verifying the schema first.
    /// </summary>
    /// <param name="connectionString">Target PostgreSQL connection string.</param>
    /// <param name="logger">Logger for the diagnostics, the schema diff and the row counts.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>
    /// The outcome. Failures are returned, not thrown, so the caller can map the exit
    /// code. A scratch database that could not be dropped is reported as a failure:
    /// a leftover database is not a clean outcome.
    /// </returns>
    public static async Task<BaselineOutcome> AdoptAsync(
        string connectionString, ILogger logger, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var scope = new ScratchDatabaseScope(target, BuildScratchDatabaseName());

        BaselineOutcome outcome;
        try
        {
            outcome = await AdoptCoreAsync(scope, logger, ct);
        }
        catch (OperationCanceledException ex)
        {
            // Cancellation is not a schema failure, but the baseline did not finish, so
            // it is a failed deployment step.
            logger.LogError("Legacy baseline CANCELLED: {Message}", OneLine(ex.Message));
            outcome = FailedOutcome($"The baseline was cancelled: {OneLine(ex.Message)}");
        }
        catch (Exception ex)
        {
            // The tool must never throw out of a deployment entrypoint: an unhandled
            // exception would abort the process without an exit code from the contract.
            // Environment-level problems (wrong password, unreachable host, command
            // timeout) get the same one-line treatment as the migration runner: a stack
            // trace buries the single line that says what to fix. A schema failure keeps
            // its stack trace, because that is where the diagnostic value is.
            if (OpsBridgeMigrationRunner.IsEnvironmentFailure(ex))
            {
                logger.LogError("Legacy baseline FAILED: {Message}", OneLine(ex.Message));
            }
            else
            {
                logger.LogError(ex, "Legacy baseline FAILED: {Message}", OneLine(ex.Message));
            }

            outcome = FailedOutcome(OneLine(ex.Message));
        }

        // The scratch database is dropped on EVERY path, including the failure paths.
        // A failed drop is not swallowed: it is logged as a warning AND reported in the
        // outcome as a failure, because a leftover scratch database means the run did
        // not clean up after itself.
        if (scope.Created)
        {
            try
            {
                await DropScratchDatabaseAsync(scope.Target, scope.Name, CancellationToken.None);
                logger.LogInformation("Dropped scratch database \"{Database}\".", scope.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to drop the scratch database \"{Database}\": {Message}. Remove it manually. " +
                    "A leftover scratch database is reported as a failure, not as a success.",
                    scope.Name,
                    OneLine(ex.Message));

                outcome = outcome with
                {
                    ExitCode = OpsBridgeMigrationRunner.ExitCodeFailure,
                    Failure =
                        $"{outcome.Failure} The scratch database \"{scope.Name}\" could not be dropped: {OneLine(ex.Message)}".Trim()
                };
            }
        }

        return outcome;
    }

    /// <summary>
    /// The EF Core runtime version, read from the EF assembly at runtime.
    /// </summary>
    /// <remarks>
    /// This is a cross-check value, not the value that is written: the baseline copies
    /// the <c>ProductVersion</c> that EF itself wrote into the scratch database, because
    /// that is by definition the value EF considers correct for this runtime.
    /// </remarks>
    internal static string ResolveEfProductVersion()
    {
        var assembly = typeof(DbContext).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    // ------------------------------------------------------------------
    // The adoption itself
    // ------------------------------------------------------------------

    private static async Task<BaselineOutcome> AdoptCoreAsync(
        ScratchDatabaseScope scope,
        ILogger logger,
        CancellationToken ct)
    {
        var target = scope.Target;

        await using var targetConnection = new NpgsqlConnection(target.ConnectionString);
        await targetConnection.OpenAsync(ct);

        // --------------------------------------------------------------
        // 1. Already managed? Then it is VERIFIED, not blessed.
        // --------------------------------------------------------------
        var existingHistory = await ReadHistoryRowsAsync(targetConnection, null, ct);
        if (existingHistory.Count > 0)
        {
            return await VerifyAlreadyManagedAsync(scope, targetConnection, existingHistory, logger, ct);
        }

        // --------------------------------------------------------------
        // 2. Capture the business data fingerprint BEFORE anything is written.
        // --------------------------------------------------------------
        var rowCountsBefore = await CaptureRowCountsAsync(targetConnection, null, ct);
        logger.LogInformation(
            "Captured pre-write row counts for {Count} user table(s).",
            rowCountsBefore.Count);
        foreach (var rowCount in rowCountsBefore)
        {
            logger.LogDebug("  pre-write  {Table} = {Rows}", rowCount.Table, rowCount.Rows);
        }

        // --------------------------------------------------------------
        // 3. Create the scratch database and apply the real migration to it.
        // --------------------------------------------------------------
        var scratchCreated = await TryCreateScratchDatabaseAsync(scope, logger, ct);
        if (!scratchCreated)
        {
            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeFailure,
                AlreadyManaged: false,
                DriftDetected: false,
                HistoryWritten: false,
                VerificationRan: false,
                Differences: Array.Empty<string>(),
                PendingMigrations: Array.Empty<string>(),
                RowCountsBefore: rowCountsBefore,
                RowCountsAfter: null,
                Failure: "CREATE DATABASE was refused: the baseline identity needs the CREATEDB privilege.");
        }

        var scratchConnectionString = new NpgsqlConnectionStringBuilder(target.ConnectionString)
        {
            Database = scope.Name
        }.ConnectionString;

        var scratchOptions = new DbContextOptionsBuilder<OpsBridgeDbContext>()
            .UseNpgsql(
                scratchConnectionString,
                npgsql => npgsql.CommandTimeout(OpsBridgeMigrationRunner.DefaultCommandTimeoutSeconds))
            .Options;

        await using (var scratchDb = new OpsBridgeDbContext(scratchOptions))
        {
            await scratchDb.Database.MigrateAsync(ct);
        }

        // The scratch history rows are exactly what EF itself wrote — the real runtime
        // migration id and the real runtime ProductVersion, never a literal.
        var scratchHistory = await ReadHistoryRowsAsync(scratchConnectionString, ct);
        if (scratchHistory.Count == 0)
        {
            logger.LogError(
                "The scratch database has no \"{HistoryTable}\" rows after applying the migration. " +
                "The baseline cannot determine which migrations to record.",
                HistoryTableName);

            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeFailure,
                AlreadyManaged: false,
                DriftDetected: false,
                HistoryWritten: false,
                VerificationRan: false,
                Differences: Array.Empty<string>(),
                PendingMigrations: Array.Empty<string>(),
                RowCountsBefore: rowCountsBefore,
                RowCountsAfter: null,
                Failure: "The scratch database produced no migration history rows.");
        }

        var assemblyProductVersion = ResolveEfProductVersion();
        logger.LogInformation(
            "Scratch database recorded migration(s) {MigrationIds} with ProductVersion {ProductVersion} " +
            "(EF runtime assembly version: {AssemblyVersion}).",
            string.Join(", ", scratchHistory.Select(row => row.MigrationId)),
            scratchHistory[0].ProductVersion,
            assemblyProductVersion);

        // --------------------------------------------------------------
        // 4. Verify: compare the scratch schema against the target schema.
        //    Domain objects only — the history table exists in the scratch database
        //    at this moment and not (yet) in the target.
        // --------------------------------------------------------------
        var differences = await CompareSchemasAsync(
            scratchConnectionString,
            targetConnection,
            null,
            includeHistoryTable: false,
            ct);

        if (differences.Count > 0)
        {
            foreach (var difference in differences)
            {
                logger.LogError("Schema drift: {Difference}", difference);
            }

            var rowCountsAfterDrift = await CaptureRowCountsAsync(targetConnection, null, ct);
            logger.LogError(
                "Drift detected: {Count} difference(s) between the target database and the migration schema. " +
                "Nothing was written to the target database. Baseline refused.",
                differences.Count);
            logger.LogInformation(
                "Target row counts after the refused baseline (must equal the pre-write counts): {Counts}",
                FormatRowCounts(rowCountsAfterDrift));

            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeDriftDetected,
                AlreadyManaged: false,
                DriftDetected: true,
                HistoryWritten: false,
                VerificationRan: true,
                Differences: differences,
                PendingMigrations: Array.Empty<string>(),
                RowCountsBefore: rowCountsBefore,
                RowCountsAfter: rowCountsAfterDrift,
                Failure: $"Schema drift detected: {differences.Count} difference(s).");
        }

        logger.LogInformation(
            "Schema verification passed: the target schema matches the migration schema for every compared " +
            "object class (tables, columns, types, nullability, defaults, primary keys, unique constraints, " +
            "indexes and foreign keys in the public schema).");

        // --------------------------------------------------------------
        // 5. Write EF's history table and the baseline row(s) — atomically.
        //
        //    The CREATE, the INSERTs and the post-write verification share ONE
        //    transaction. It is committed only when the verification reports zero
        //    differences; otherwise it is rolled back, so a half-written target can
        //    never be left behind.
        // --------------------------------------------------------------
        await using var transaction = await targetConnection.BeginTransactionAsync(ct);

        var historyTableExists = await HistoryTableExistsAsync(targetConnection, transaction, ct);
        if (!historyTableExists)
        {
            await ExecuteAsync(
                targetConnection,
                transaction,
                """
                CREATE TABLE "__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId"))
                """,
                ct);
        }
        else
        {
            logger.LogInformation(
                "\"{HistoryTable}\" already exists but is empty; it will be reused as-is and re-verified below.",
                HistoryTableName);
        }

        foreach (var historyRow in scratchHistory)
        {
            await ExecuteAsync(
                targetConnection,
                transaction,
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES (@migrationId, @productVersion)",
                ct,
                ("migrationId", historyRow.MigrationId),
                ("productVersion", historyRow.ProductVersion));
        }

        logger.LogInformation(
            "Wrote {Count} baseline row(s) to \"{HistoryTable}\" (not committed yet): {MigrationIds}",
            scratchHistory.Count,
            HistoryTableName,
            string.Join(", ", scratchHistory.Select(row => row.MigrationId)));

        // --------------------------------------------------------------
        // 6. Re-verify AFTER the write, this time including the history table —
        //    still inside the transaction.
        // --------------------------------------------------------------
        var postWriteDifferences = await CompareSchemasAsync(
            scratchConnectionString,
            targetConnection,
            transaction,
            includeHistoryTable: true,
            ct);

        if (postWriteDifferences.Count > 0)
        {
            foreach (var difference in postWriteDifferences)
            {
                logger.LogError("Post-write verification failed: {Difference}", difference);
            }

            logger.LogError(
                "Post-write verification found {Count} difference(s) between the target database and the " +
                "migration schema, INCLUDING the history table. The write is rolled back; the target database " +
                "is left exactly as it was. This is a hard failure.",
                postWriteDifferences.Count);

            await transaction.RollbackAsync(ct);

            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeFailure,
                AlreadyManaged: false,
                DriftDetected: false,
                HistoryWritten: false,
                VerificationRan: true,
                Differences: postWriteDifferences,
                PendingMigrations: Array.Empty<string>(),
                RowCountsBefore: rowCountsBefore,
                RowCountsAfter: null,
                Failure: $"Post-write verification failed with {postWriteDifferences.Count} difference(s).");
        }

        logger.LogInformation(
            "Post-write verification passed: the target schema (including \"{HistoryTable}\") matches the " +
            "migration schema for every compared object class.",
            HistoryTableName);

        // --------------------------------------------------------------
        // 7. Prove the business data survived — still inside the transaction.
        // --------------------------------------------------------------
        var rowCountsAfter = await CaptureRowCountsAsync(targetConnection, transaction, ct);

        if (!RowCountsMatch(rowCountsBefore, rowCountsAfter))
        {
            logger.LogError(
                "Row counts changed during the baseline: before {Before}, after {After}. The write is rolled back. " +
                "This is a hard failure.",
                FormatRowCounts(rowCountsBefore),
                FormatRowCounts(rowCountsAfter));

            await transaction.RollbackAsync(ct);

            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeFailure,
                AlreadyManaged: false,
                DriftDetected: false,
                HistoryWritten: false,
                VerificationRan: true,
                Differences: Array.Empty<string>(),
                PendingMigrations: Array.Empty<string>(),
                RowCountsBefore: rowCountsBefore,
                RowCountsAfter: rowCountsAfter,
                Failure: "Row counts changed during the baseline.");
        }

        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Committed the baseline. Business data preserved: all {Count} user table(s) have unchanged row counts.",
            rowCountsAfter.Count);

        return new BaselineOutcome(
            OpsBridgeMigrationRunner.ExitCodeSuccess,
            AlreadyManaged: false,
            DriftDetected: false,
            HistoryWritten: true,
            VerificationRan: true,
            Differences: Array.Empty<string>(),
            PendingMigrations: Array.Empty<string>(),
            RowCountsBefore: rowCountsBefore,
            RowCountsAfter: rowCountsAfter,
            Failure: null);
    }

    // ------------------------------------------------------------------
    // The "already managed" path — verified, never blessed
    // ------------------------------------------------------------------

    private static async Task<BaselineOutcome> VerifyAlreadyManagedAsync(
        ScratchDatabaseScope scope,
        NpgsqlConnection targetConnection,
        IReadOnlyList<(string MigrationId, string ProductVersion)> existingHistory,
        ILogger logger,
        CancellationToken ct)
    {
        var recordedIds = existingHistory.Select(row => row.MigrationId).ToList();
        var compiledMigrations = GetCompiledMigrations(scope.Target.ConnectionString);

        logger.LogInformation(
            "Target database already has {Count} row(s) in \"{HistoryTable}\" ({MigrationIds}). " +
            "Verifying it against the migrations this build compiled before reporting success.",
            existingHistory.Count,
            HistoryTableName,
            string.Join(", ", recordedIds));

        // --------------------------------------------------------------
        // Unknown id => unknown state => refuse. A migration id this build does not
        // know cannot be verified, and a database that carries one was written by
        // something else (or by a newer build) — that is drift, not success.
        // --------------------------------------------------------------
        var unknownIds = recordedIds
            .Where(id => !compiledMigrations.Contains(id, StringComparer.Ordinal))
            .ToList();

        if (unknownIds.Count > 0)
        {
            var unknownDifferences = unknownIds
                .Select(id => $"the history table records migration \"{id}\", which this build does not know")
                .ToList();

            foreach (var difference in unknownDifferences)
            {
                logger.LogError("Unknown migration state: {Difference}", difference);
            }

            logger.LogError(
                "The target records {Count} migration id(s) this build does not know: {UnknownIds}. " +
                "The state of the database is unknown, so the baseline refuses it.",
                unknownIds.Count,
                string.Join(", ", unknownIds));

            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeDriftDetected,
                AlreadyManaged: true,
                DriftDetected: true,
                HistoryWritten: false,
                VerificationRan: false,
                Differences: unknownDifferences,
                PendingMigrations: Array.Empty<string>(),
                RowCountsBefore: Array.Empty<TableRowCount>(),
                RowCountsAfter: null,
                Failure: $"Unknown migration id(s) in the history table: {string.Join(", ", unknownIds)}.");
        }

        // --------------------------------------------------------------
        // The recorded ids must be a PREFIX of the compiled migrations: a history
        // that skips a migration cannot be reproduced with "migrate up to X", and a
        // history that is out of order cannot be verified at all.
        // --------------------------------------------------------------
        var expectedPrefix = compiledMigrations.Take(recordedIds.Count).ToList();
        if (!expectedPrefix.SequenceEqual(recordedIds, StringComparer.Ordinal))
        {
            var message =
                "The recorded migration history is not a prefix of the migrations this build compiled " +
                $"(recorded: {string.Join(", ", recordedIds)}; compiled: {string.Join(", ", compiledMigrations)}). " +
                "The state of the database is unknown, so the baseline refuses it.";

            logger.LogError("{Message}", message);

            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeDriftDetected,
                AlreadyManaged: true,
                DriftDetected: true,
                HistoryWritten: false,
                VerificationRan: false,
                Differences: new[] { message },
                PendingMigrations: Array.Empty<string>(),
                RowCountsBefore: Array.Empty<TableRowCount>(),
                RowCountsAfter: null,
                Failure: "The recorded migration history is not a prefix of the compiled migrations.");
        }

        var pendingMigrations = compiledMigrations.Skip(recordedIds.Count).ToList();
        var targetMigration = recordedIds[^1];

        // --------------------------------------------------------------
        // Compare the target against a scratch database migrated UP TO the same
        // migration. That is the only schema the target is allowed to have.
        // --------------------------------------------------------------
        var rowCountsBefore = await CaptureRowCountsAsync(targetConnection, null, ct);

        var scratchCreated = await TryCreateScratchDatabaseAsync(scope, logger, ct);
        if (!scratchCreated)
        {
            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeFailure,
                AlreadyManaged: true,
                DriftDetected: false,
                HistoryWritten: false,
                VerificationRan: false,
                Differences: Array.Empty<string>(),
                PendingMigrations: pendingMigrations,
                RowCountsBefore: rowCountsBefore,
                RowCountsAfter: null,
                Failure: "CREATE DATABASE was refused: the baseline identity needs the CREATEDB privilege.");
        }

        var scratchConnectionString = new NpgsqlConnectionStringBuilder(scope.Target.ConnectionString)
        {
            Database = scope.Name
        }.ConnectionString;

        var scratchOptions = new DbContextOptionsBuilder<OpsBridgeDbContext>()
            .UseNpgsql(
                scratchConnectionString,
                npgsql => npgsql.CommandTimeout(OpsBridgeMigrationRunner.DefaultCommandTimeoutSeconds))
            .Options;

        await using (var scratchDb = new OpsBridgeDbContext(scratchOptions))
        {
            await scratchDb.Database.MigrateAsync(targetMigration, ct);
        }

        var differences = await CompareSchemasAsync(
            scratchConnectionString,
            targetConnection,
            null,
            includeHistoryTable: false,
            ct);

        if (differences.Count > 0)
        {
            foreach (var difference in differences)
            {
                logger.LogError("Schema drift: {Difference}", difference);
            }

            logger.LogError(
                "The target database is already managed but its schema does not match migration \"{Migration}\". " +
                "{Count} difference(s) found. Nothing was written.",
                targetMigration,
                differences.Count);

            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeDriftDetected,
                AlreadyManaged: true,
                DriftDetected: true,
                HistoryWritten: false,
                VerificationRan: true,
                Differences: differences,
                PendingMigrations: pendingMigrations,
                RowCountsBefore: rowCountsBefore,
                RowCountsAfter: await CaptureRowCountsAsync(targetConnection, null, ct),
                Failure: $"Schema drift detected: {differences.Count} difference(s).");
        }

        var rowCountsAfter = await CaptureRowCountsAsync(targetConnection, null, ct);
        if (!RowCountsMatch(rowCountsBefore, rowCountsAfter))
        {
            logger.LogError(
                "Row counts changed while the already-managed database was verified: before {Before}, after {After}. " +
                "This is a hard failure.",
                FormatRowCounts(rowCountsBefore),
                FormatRowCounts(rowCountsAfter));

            return new BaselineOutcome(
                OpsBridgeMigrationRunner.ExitCodeFailure,
                AlreadyManaged: true,
                DriftDetected: false,
                HistoryWritten: false,
                VerificationRan: true,
                Differences: Array.Empty<string>(),
                PendingMigrations: pendingMigrations,
                RowCountsBefore: rowCountsBefore,
                RowCountsAfter: rowCountsAfter,
                Failure: "Row counts changed while the already-managed database was verified.");
        }

        logger.LogInformation(
            "Verification passed: the already-managed target matches migration \"{Migration}\" for every compared " +
            "object class. Nothing was written.",
            targetMigration);

        if (pendingMigrations.Count > 0)
        {
            logger.LogInformation(
                "{Count} migration(s) remain pending for the normal --migrate path: {Migrations}",
                pendingMigrations.Count,
                string.Join(", ", pendingMigrations));
        }
        else
        {
            logger.LogInformation("No migration remains pending; the database is at head.");
        }

        return new BaselineOutcome(
            OpsBridgeMigrationRunner.ExitCodeSuccess,
            AlreadyManaged: true,
            DriftDetected: false,
            HistoryWritten: false,
            VerificationRan: true,
            Differences: Array.Empty<string>(),
            PendingMigrations: pendingMigrations,
            RowCountsBefore: rowCountsBefore,
            RowCountsAfter: rowCountsAfter,
            Failure: null);
    }

    // ------------------------------------------------------------------
    // Scratch database
    // ------------------------------------------------------------------

    /// <summary>
    /// Create the scratch database. Returns false when the server refused the
    /// <c>CREATE DATABASE</c> — the fail-closed case: without the scratch database
    /// the schema cannot be verified, and an unverified baseline is never written.
    /// </summary>
    private static async Task<bool> TryCreateScratchDatabaseAsync(
        ScratchDatabaseScope scope,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            await CreateScratchDatabaseAsync(scope.Target, scope.Name, ct);
        }
        catch (Exception ex) when (IsPrivilegeFailure(ex))
        {
            logger.LogError(
                "CREATE DATABASE was refused for the scratch database \"{Database}\". " +
                "The baseline cannot verify the schema without it, so nothing was written to the target. " +
                "Grant CREATEDB to the identity running this tool, or adopt the database with a " +
                "DDL-capable deployment identity.",
                scope.Name);

            return false;
        }

        scope.Created = true;
        logger.LogInformation("Created scratch database \"{Database}\" for schema verification.", scope.Name);

        return true;
    }

    private static async Task CreateScratchDatabaseAsync(
        NpgsqlConnectionStringBuilder target, string scratchDatabase, CancellationToken ct)
    {
        Exception? lastFailure = null;

        foreach (var maintenanceDatabase in new[] { "postgres", "template1" })
        {
            var maintenance = new NpgsqlConnectionStringBuilder(target.ConnectionString)
            {
                Database = maintenanceDatabase
            };

            try
            {
                await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
                await connection.OpenAsync(ct);
                await ExecuteAsync(
                    connection,
                    null,
                    $"CREATE DATABASE {QuoteIdentifier(scratchDatabase)}",
                    ct);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == "3D000")
            {
                // This maintenance database does not exist on this server; try the next one.
                lastFailure = ex;
            }
        }

        throw new InvalidOperationException(
            "Could not connect to a maintenance database (postgres / template1) to create the scratch database.",
            lastFailure);
    }

    private static async Task DropScratchDatabaseAsync(
        NpgsqlConnectionStringBuilder target, string scratchDatabase, CancellationToken ct)
    {
        Exception? lastFailure = null;

        foreach (var maintenanceDatabase in new[] { "postgres", "template1" })
        {
            var maintenance = new NpgsqlConnectionStringBuilder(target.ConnectionString)
            {
                Database = maintenanceDatabase
            };

            try
            {
                await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
                await connection.OpenAsync(ct);

                // WITH (FORCE) terminates leftover sessions on the scratch database, which
                // would otherwise block the drop. It requires PostgreSQL 13 or newer.
                await ExecuteAsync(
                    connection,
                    null,
                    $"DROP DATABASE IF EXISTS {QuoteIdentifier(scratchDatabase)} WITH (FORCE)",
                    ct);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == "3D000")
            {
                // This maintenance database does not exist on this server; try the next one.
                lastFailure = ex;
            }
        }

        throw new InvalidOperationException(
            "Could not connect to a maintenance database (postgres / template1) to drop the scratch database.",
            lastFailure);
    }

    // ------------------------------------------------------------------
    // Database state readers
    // ------------------------------------------------------------------

    private static async Task<bool> HistoryTableExistsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relname = '__EFMigrationsHistory'
                  AND c.relkind IN ('r', 'p'))
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var result = await command.ExecuteScalarAsync(ct);
        return result is true;
    }

    private static async Task<List<(string MigrationId, string ProductVersion)>> ReadHistoryRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct)
    {
        var rows = new List<(string MigrationId, string ProductVersion)>();

        if (!await HistoryTableExistsAsync(connection, transaction, ct))
        {
            return rows;
        }

        const string sql = """
            SELECT "MigrationId", "ProductVersion"
            FROM "__EFMigrationsHistory"
            ORDER BY "MigrationId"
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<(string MigrationId, string ProductVersion)>> ReadHistoryRowsAsync(
        string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return await ReadHistoryRowsAsync(connection, null, ct);
    }

    /// <summary>
    /// The migration ids THIS build compiled, in application order. Read from the
    /// migrations assembly — no database round trip.
    /// </summary>
    private static IReadOnlyList<string> GetCompiledMigrations(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OpsBridgeDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(OpsBridgeMigrationRunner.DefaultCommandTimeoutSeconds))
            .Options;

        using var db = new OpsBridgeDbContext(options);
        return db.Database.GetMigrations().ToList();
    }

    private static async Task<IReadOnlyList<TableRowCount>> CaptureRowCountsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct)
    {
        var tables = await QueryAsync(
            connection,
            transaction,
            """
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND c.relkind IN ('r', 'p')
              AND c.relname <> '__EFMigrationsHistory'
            ORDER BY c.relname
            """,
            ct);

        var counts = new List<TableRowCount>();
        foreach (var table in tables)
        {
            var tableName = table[0];
            await using var command = new NpgsqlCommand(
                $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}",
                connection,
                transaction);
            var value = await command.ExecuteScalarAsync(ct);
            counts.Add(new TableRowCount(tableName, Convert.ToInt64(value ?? 0L)));
        }

        return counts;
    }

    private static bool RowCountsMatch(
        IReadOnlyList<TableRowCount> before, IReadOnlyList<TableRowCount> after)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        for (var i = 0; i < before.Count; i++)
        {
            if (!string.Equals(before[i].Table, after[i].Table, StringComparison.Ordinal)
                || before[i].Rows != after[i].Rows)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatRowCounts(IReadOnlyList<TableRowCount> counts)
        => string.Join(", ", counts.Select(count => $"{count.Table}={count.Rows}"));

    // ------------------------------------------------------------------
    // Schema capture and comparison
    // ------------------------------------------------------------------

    private static async Task<IReadOnlyList<string>> CompareSchemasAsync(
        string expectedConnectionString,
        NpgsqlConnection actualConnection,
        NpgsqlTransaction? actualTransaction,
        bool includeHistoryTable,
        CancellationToken ct)
    {
        var expected = await CaptureSchemaAsync(expectedConnectionString, ct);
        var actual = await CaptureSchemaAsync(actualConnection, actualTransaction, ct);

        var differences = new List<string>();

        var expectedTables = expected.Tables
            .Where(table => includeHistoryTable || !string.Equals(table, HistoryTableName, StringComparison.Ordinal))
            .ToList();
        var actualTables = actual.Tables
            .Where(table => includeHistoryTable || !string.Equals(table, HistoryTableName, StringComparison.Ordinal))
            .ToList();

        foreach (var table in expectedTables)
        {
            if (!actualTables.Contains(table, StringComparer.Ordinal))
            {
                differences.Add($"table \"{table}\" is missing in the target database");
                continue;
            }

            differences.AddRange(CompareColumns(table, expected, actual));
            differences.AddRange(ComparePrimaryKey(table, expected, actual));
            differences.AddRange(CompareUniqueConstraints(table, expected, actual));
            differences.AddRange(CompareIndexes(table, expected, actual));
            differences.AddRange(CompareForeignKeys(table, expected, actual));
        }

        foreach (var table in actualTables)
        {
            if (!expectedTables.Contains(table, StringComparer.Ordinal))
            {
                differences.Add($"table \"{table}\" exists in the target database but not in the migration schema");
            }
        }

        return differences;
    }

    private static IEnumerable<string> CompareColumns(
        string table, SchemaSnapshot expected, SchemaSnapshot actual)
    {
        var expectedColumns = expected.Columns[table];
        var actualColumns = actual.Columns[table];

        foreach (var (name, expectedColumn) in expectedColumns)
        {
            if (!actualColumns.TryGetValue(name, out var actualColumn))
            {
                yield return $"table \"{table}\" is missing column \"{name}\" (expected type {expectedColumn.DataType})";
                continue;
            }

            if (!string.Equals(expectedColumn.DataType, actualColumn.DataType, StringComparison.Ordinal))
            {
                yield return $"table \"{table}\" column \"{name}\" type mismatch: target is \"{actualColumn.DataType}\", migration expects \"{expectedColumn.DataType}\"";
            }

            if (expectedColumn.Nullable != actualColumn.Nullable)
            {
                yield return $"table \"{table}\" column \"{name}\" nullability mismatch: target is {(actualColumn.Nullable ? "NULL" : "NOT NULL")}, migration expects {(expectedColumn.Nullable ? "NULL" : "NOT NULL")}";
            }

            if (!string.Equals(expectedColumn.Default, actualColumn.Default, StringComparison.Ordinal))
            {
                yield return $"table \"{table}\" column \"{name}\" default mismatch: target is \"{actualColumn.Default}\", migration expects \"{expectedColumn.Default}\"";
            }
        }

        foreach (var name in actualColumns.Keys)
        {
            if (!expectedColumns.ContainsKey(name))
            {
                yield return $"table \"{table}\" has column \"{name}\" that the migration schema does not define";
            }
        }
    }

    private static IEnumerable<string> ComparePrimaryKey(
        string table, SchemaSnapshot expected, SchemaSnapshot actual)
    {
        expected.PrimaryKeys.TryGetValue(table, out var expectedKey);
        actual.PrimaryKeys.TryGetValue(table, out var actualKey);

        if (expectedKey is null && actualKey is null)
        {
            yield break;
        }

        if (expectedKey is null)
        {
            yield return $"table \"{table}\" has primary key \"{actualKey}\" but the migration schema defines none";
            yield break;
        }

        if (actualKey is null)
        {
            yield return $"table \"{table}\" is missing its primary key ({expectedKey})";
            yield break;
        }

        if (!string.Equals(expectedKey, actualKey, StringComparison.Ordinal))
        {
            yield return $"table \"{table}\" primary key mismatch: target is \"{actualKey}\", migration expects \"{expectedKey}\"";
        }
    }

    private static IEnumerable<string> CompareUniqueConstraints(
        string table, SchemaSnapshot expected, SchemaSnapshot actual)
    {
        foreach (var (key, expectedDefinition) in expected.UniqueConstraints.Where(pair => pair.Key.StartsWith(table + "|", StringComparison.Ordinal)))
        {
            if (!actual.UniqueConstraints.TryGetValue(key, out var actualDefinition))
            {
                yield return $"table \"{table}\" is missing unique constraint \"{ConstraintName(key)}\" ({expectedDefinition})";
                continue;
            }

            if (!string.Equals(expectedDefinition, actualDefinition, StringComparison.Ordinal))
            {
                yield return $"table \"{table}\" unique constraint \"{ConstraintName(key)}\" mismatch: target is \"{actualDefinition}\", migration expects \"{expectedDefinition}\"";
            }
        }

        foreach (var key in actual.UniqueConstraints.Keys.Where(key => key.StartsWith(table + "|", StringComparison.Ordinal)))
        {
            if (!expected.UniqueConstraints.ContainsKey(key))
            {
                yield return $"table \"{table}\" has unique constraint \"{ConstraintName(key)}\" that the migration schema does not define";
            }
        }
    }

    private static IEnumerable<string> CompareIndexes(
        string table, SchemaSnapshot expected, SchemaSnapshot actual)
    {
        foreach (var (key, expectedDefinition) in expected.Indexes.Where(pair => pair.Key.StartsWith(table + "|", StringComparison.Ordinal)))
        {
            if (!actual.Indexes.TryGetValue(key, out var actualDefinition))
            {
                yield return $"table \"{table}\" is missing index \"{ConstraintName(key)}\" ({expectedDefinition})";
                continue;
            }

            if (!string.Equals(expectedDefinition, actualDefinition, StringComparison.Ordinal))
            {
                yield return $"table \"{table}\" index \"{ConstraintName(key)}\" mismatch: target is \"{actualDefinition}\", migration expects \"{expectedDefinition}\"";
            }
        }

        foreach (var key in actual.Indexes.Keys.Where(key => key.StartsWith(table + "|", StringComparison.Ordinal)))
        {
            if (!expected.Indexes.ContainsKey(key))
            {
                yield return $"table \"{table}\" has index \"{ConstraintName(key)}\" that the migration schema does not define";
            }
        }
    }

    private static IEnumerable<string> CompareForeignKeys(
        string table, SchemaSnapshot expected, SchemaSnapshot actual)
    {
        foreach (var (key, expectedDefinition) in expected.ForeignKeys.Where(pair => pair.Key.StartsWith(table + "|", StringComparison.Ordinal)))
        {
            if (!actual.ForeignKeys.TryGetValue(key, out var actualDefinition))
            {
                yield return $"table \"{table}\" is missing foreign key \"{ConstraintName(key)}\" ({expectedDefinition})";
                continue;
            }

            if (!string.Equals(expectedDefinition, actualDefinition, StringComparison.Ordinal))
            {
                yield return $"table \"{table}\" foreign key \"{ConstraintName(key)}\" mismatch: target is \"{actualDefinition}\", migration expects \"{expectedDefinition}\"";
            }
        }

        foreach (var key in actual.ForeignKeys.Keys.Where(key => key.StartsWith(table + "|", StringComparison.Ordinal)))
        {
            if (!expected.ForeignKeys.ContainsKey(key))
            {
                yield return $"table \"{table}\" has foreign key \"{ConstraintName(key)}\" that the migration schema does not define";
            }
        }
    }

    private static string ConstraintName(string key)
    {
        var separator = key.IndexOf('|');
        return separator >= 0 ? key[(separator + 1)..] : key;
    }

    private static async Task<SchemaSnapshot> CaptureSchemaAsync(
        string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        return await CaptureSchemaAsync(connection, null, ct);
    }

    private static async Task<SchemaSnapshot> CaptureSchemaAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct)
    {
        var snapshot = new SchemaSnapshot();

        foreach (var row in await QueryAsync(
            connection,
            transaction,
            """
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND c.relkind IN ('r', 'p')
            ORDER BY c.relname
            """,
            ct))
        {
            snapshot.Tables.Add(row[0]);
        }

        foreach (var row in await QueryAsync(
            connection,
            transaction,
            """
            SELECT c.relname,
                   a.attname,
                   format_type(a.atttypid, a.atttypmod),
                   CASE WHEN a.attnotnull THEN 'NO' ELSE 'YES' END,
                   COALESCE(pg_get_expr(d.adbin, d.adrelid), '')
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE n.nspname = 'public'
              AND c.relkind IN ('r', 'p')
              AND a.attnum > 0
              AND NOT a.attisdropped
            ORDER BY c.relname, a.attname
            """,
            ct))
        {
            var table = row[0];
            if (!snapshot.Columns.TryGetValue(table, out var tableColumns))
            {
                tableColumns = new SortedDictionary<string, ColumnInfo>(StringComparer.Ordinal);
                snapshot.Columns[table] = tableColumns;
            }

            tableColumns[row[1]] =
                new ColumnInfo(row[1], row[2], string.Equals(row[3], "YES", StringComparison.Ordinal), row[4]);
        }

        foreach (var row in await QueryAsync(
            connection,
            transaction,
            """
            SELECT c.relname, con.conname, pg_get_constraintdef(con.oid)
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND con.contype = 'p'
            ORDER BY c.relname, con.conname
            """,
            ct))
        {
            snapshot.PrimaryKeys[row[0]] = row[2];
        }

        var uniqueConstraintNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in await QueryAsync(
            connection,
            transaction,
            """
            SELECT c.relname, con.conname, pg_get_constraintdef(con.oid)
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND con.contype = 'u'
            ORDER BY c.relname, con.conname
            """,
            ct))
        {
            uniqueConstraintNames.Add(row[1]);
            snapshot.UniqueConstraints[Key(row[0], row[1])] = row[2];
        }

        foreach (var row in await QueryAsync(
            connection,
            transaction,
            """
            SELECT c.relname, i.relname, pg_get_indexdef(x.indexrelid)
            FROM pg_index x
            JOIN pg_class c ON c.oid = x.indrelid
            JOIN pg_class i ON i.oid = x.indexrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND NOT x.indisprimary
            ORDER BY c.relname, i.relname
            """,
            ct))
        {
            // A unique constraint also owns a backing index; reporting both would
            // duplicate the same difference.
            if (uniqueConstraintNames.Contains(row[1]))
            {
                continue;
            }

            snapshot.Indexes[Key(row[0], row[1])] = row[2];
        }

        foreach (var row in await QueryAsync(
            connection,
            transaction,
            """
            SELECT c.relname, con.conname, pg_get_constraintdef(con.oid)
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND con.contype = 'f'
            ORDER BY c.relname, con.conname
            """,
            ct))
        {
            snapshot.ForeignKeys[Key(row[0], row[1])] = row[2];
        }

        return snapshot;
    }

    private static string Key(string table, string name) => $"{table}|{name}";

    private static async Task<List<string[]>> QueryAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken ct)
    {
        var rows = new List<string[]>();

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = reader.IsDBNull(i) ? string.Empty : reader.GetString(i);
            }

            rows.Add(values);
        }

        return rows;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static BaselineOutcome FailedOutcome(string failure)
        => new(
            OpsBridgeMigrationRunner.ExitCodeFailure,
            AlreadyManaged: false,
            DriftDetected: false,
            HistoryWritten: false,
            VerificationRan: false,
            Differences: Array.Empty<string>(),
            PendingMigrations: Array.Empty<string>(),
            RowCountsBefore: Array.Empty<TableRowCount>(),
            RowCountsAfter: null,
            Failure: failure);

    private static string BuildScratchDatabaseName()
        => $"opsbridge_baseline_check_{Guid.NewGuid():N}";

    private static bool IsPrivilegeFailure(Exception ex)
        => ex is PostgresException postgresException
           && (postgresException.SqlState == "42501"  // insufficient_privilege
               || postgresException.SqlState == "53000"  // insufficient_resources
               || postgresException.SqlState == "28000"); // invalid_authorization_specification

    private static string OneLine(string message)
        => string.IsNullOrWhiteSpace(message)
            ? "(no message)"
            : message.ReplaceLineEndings(" ").Trim();

    private sealed record ColumnInfo(string Name, string DataType, bool Nullable, string Default);

    /// <summary>
    /// The scratch database this run owns. <see cref="Created"/> is what makes the drop
    /// unconditional: the database is dropped on every path, and a failed drop is
    /// reported as a failure.
    /// </summary>
    private sealed class ScratchDatabaseScope
    {
        public ScratchDatabaseScope(NpgsqlConnectionStringBuilder target, string name)
        {
            Target = target;
            Name = name;
        }

        public NpgsqlConnectionStringBuilder Target { get; }

        public string Name { get; }

        public bool Created { get; set; }
    }

    private sealed class SchemaSnapshot
    {
        public List<string> Tables { get; } = new();

        public Dictionary<string, SortedDictionary<string, ColumnInfo>> Columns { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> PrimaryKeys { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> UniqueConstraints { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Indexes { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> ForeignKeys { get; } = new(StringComparer.Ordinal);
    }
}
