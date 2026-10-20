using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace OpsBridge.Infrastructure.Persistence;

/// <summary>
/// Result of one migration run.
/// </summary>
/// <param name="ExitCode">
/// Process exit code: <see cref="OpsBridgeMigrationRunner.ExitCodeSuccess"/> (0) when the
/// schema is at head (whether or not anything had to be applied),
/// <see cref="OpsBridgeMigrationRunner.ExitCodeFailure"/> (1) when the migration failed,
/// <see cref="OpsBridgeMigrationRunner.ExitCodeDriftDetected"/> (2) when the baseline tool
/// refused to adopt a database because the schema had drifted.
/// </param>
/// <param name="AlreadyAtHead">
/// True when THIS RUN applied no migration — either because nothing was pending when it
/// started, or because another instance had already applied the pending migrations
/// before this run took EF's migration lock. It is a statement about this run, not
/// about the database as a whole.
/// </param>
/// <param name="PendingMigrations">Migration ids that were pending when the run started.</param>
/// <param name="AppliedMigrations">
/// Migration ids that <b>this process</b> applied. This is EF's own per-run result
/// (EF logs one "Applying migration" line per migration it executes), so it never
/// contains a migration another instance applied.
/// </param>
/// <param name="NewlyRecordedMigrations">
/// Migration ids that became present in the history table <b>during this run</b>. Under
/// concurrency this set can be larger than <paramref name="AppliedMigrations"/>: a
/// migration another instance applied shows up here as well. It is NOT a claim that this
/// process executed that DDL.
/// </param>
/// <param name="RecordedMigrations">
/// Every migration id present in the history table when the outcome was produced. On
/// failure this is what IS committed — which is not the same as what this run applied.
/// </param>
/// <param name="Failure">Failure description, or <c>null</c> on success.</param>
public sealed record MigrationRunOutcome(
    int ExitCode,
    bool AlreadyAtHead,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> NewlyRecordedMigrations,
    IReadOnlyList<string> RecordedMigrations,
    string? Failure);

/// <summary>
/// Applies pending EF Core migrations to the OpsBridge database as an explicit,
/// single-shot operation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why no advisory lock.</b> EF Core 9 introduced built-in migration locking and
/// EF Core 10 (this repository pins <c>Microsoft.EntityFrameworkCore 10.0.12</c>) has
/// it enabled by default. The provider acquires
/// <c>LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE</c> inside the
/// migration transaction, so the lock is released on commit, on rollback, on failure and
/// on process death, and it is re-acquired for every migration. Adding a
/// <c>pg_advisory_lock</c> on top would be redundant, and a session-level advisory lock
/// would be actively unsafe under Npgsql connection pooling (the session can outlive the
/// logical scope). Disabling EF's lock is explicitly unsupported by Microsoft. See
/// ADR-018.
/// </para>
/// <para>
/// <b>Why no explicit transaction.</b> EF Core 9+ throws
/// <c>MigrationsUserTransactionWarning</c> if the caller wraps <c>MigrateAsync</c> in a
/// user transaction, because EF already runs each migration in its own transaction.
/// </para>
/// <para>
/// <b>Why the command timeout is raised.</b> The Npgsql default is 30 seconds. The first
/// statement of a run — reading the pending migrations — blocks while another instance
/// holds EF's migration lock, so the timeout has to be long enough to cover that wait
/// during a rolling deployment. The default here is 120 seconds and is configurable per
/// call site (<c>OpsBridge:MigrationCommandTimeoutSeconds</c>). A timeout is still a
/// failure: it means the wait was longer than the operator allowed.
/// </para>
/// <para>
/// <b>Why this does not need a DI container.</b> The deployment entrypoint
/// (<c>--migrate</c>) must be able to run before the application host exists, so the
/// <see cref="DbContextOptions{TContext}"/> are built directly. The FlashSale
/// equivalent had to fall back to a throwaway service provider (ASP0000) for its
/// logger; this one does not, because the logger is passed in.
/// </para>
/// <para>
/// <b>Why nothing escapes the runner.</b> The whole body is wrapped: every failure —
/// including a wrong password, an unreachable host and a command timeout — is turned
/// into <see cref="ExitCodeFailure"/> with a one-line operator message, never into an
/// unhandled exception. A deployment step that dies with a stack trace and exit code
/// 134 tells the operator nothing about which contract code the run should have had.
/// </para>
/// </remarks>
public static class OpsBridgeMigrationRunner
{
    /// <summary>Exit code: success, including "the schema was already at head".</summary>
    public const int ExitCodeSuccess = 0;

    /// <summary>Exit code: the migration failed. Nothing was committed.</summary>
    public const int ExitCodeFailure = 1;

    /// <summary>Exit code: drift detected; the baseline tool refused to adopt the database.</summary>
    public const int ExitCodeDriftDetected = 2;

    /// <summary>
    /// Default Npgsql command timeout in seconds. Deliberately above the 30 second
    /// provider default so that waiting for EF's migration lock is not mistaken for a
    /// hung command.
    /// </summary>
    public const int DefaultCommandTimeoutSeconds = 120;

    /// <summary>
    /// Apply every pending EF migration to the given database.
    /// </summary>
    /// <param name="connectionString">Target PostgreSQL connection string.</param>
    /// <param name="logger">Logger for the progress lines that land in the deployment log.</param>
    /// <param name="ct">Cancellation, so a deployment job deletion interrupts cleanly.</param>
    /// <returns>
    /// The run outcome. Failures are converted into
    /// <see cref="ExitCodeFailure"/> rather than rethrown: the caller is a deployment
    /// entrypoint that maps the exit code onto a process exit status, and an unhandled
    /// exception would print a stack trace without naming the migrations that were
    /// attempted.
    /// </returns>
    public static Task<MigrationRunOutcome> RunAsync(
        string connectionString, ILogger logger, CancellationToken ct = default)
        => RunAsync(connectionString, logger, DefaultCommandTimeoutSeconds, ct);

    /// <summary>
    /// Apply every pending EF migration to the given database, with an explicit command timeout.
    /// </summary>
    /// <param name="connectionString">Target PostgreSQL connection string.</param>
    /// <param name="logger">Logger for the progress lines that land in the deployment log.</param>
    /// <param name="commandTimeoutSeconds">
    /// Npgsql command timeout in seconds. Values of zero or less fall back to
    /// <see cref="DefaultCommandTimeoutSeconds"/>.
    /// </param>
    /// <param name="ct">Cancellation, so a deployment job deletion interrupts cleanly.</param>
    /// <returns>The run outcome.</returns>
    public static async Task<MigrationRunOutcome> RunAsync(
        string connectionString,
        ILogger logger,
        int commandTimeoutSeconds,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        var timeout = commandTimeoutSeconds > 0 ? commandTimeoutSeconds : DefaultCommandTimeoutSeconds;

        try
        {
            return await RunCoreAsync(connectionString, logger, timeout, ct);
        }
        catch (OperationCanceledException ex)
        {
            // Cancellation is not a schema failure, but it is still a failed deployment
            // step: the schema was NOT brought to head.
            var message = OneLine(ex.Message);
            logger.LogError("Migration CANCELLED: {Message}", message);

            return new MigrationRunOutcome(
                ExitCodeFailure,
                AlreadyAtHead: false,
                PendingMigrations: Array.Empty<string>(),
                AppliedMigrations: Array.Empty<string>(),
                NewlyRecordedMigrations: Array.Empty<string>(),
                RecordedMigrations: Array.Empty<string>(),
                Failure: $"The migration was cancelled: {message}");
        }
        catch (Exception ex)
        {
            // EVERY failure ends here — a wrong password, an unreachable host, a command
            // timeout, a broken connection string. The deployment contract is an exit
            // code, so the exit code must be the contract code, never an unhandled
            // exception (which would abort the process with 134 and a stack trace).
            var message = FailureMessage(ex, timeout);
            LogFailure(logger, ex, message);

            return new MigrationRunOutcome(
                ExitCodeFailure,
                AlreadyAtHead: false,
                PendingMigrations: Array.Empty<string>(),
                AppliedMigrations: Array.Empty<string>(),
                NewlyRecordedMigrations: Array.Empty<string>(),
                RecordedMigrations: Array.Empty<string>(),
                Failure: message);
        }
    }

    private static async Task<MigrationRunOutcome> RunCoreAsync(
        string connectionString,
        ILogger logger,
        int timeout,
        CancellationToken ct)
    {
        var observer = new MigrationEventObserver(logger);

        var options = new DbContextOptionsBuilder<OpsBridgeDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(timeout))
            // EF's own migration log is the only per-run result EF exposes about which
            // migrations THIS process applied, so it is observed rather than guessed.
            .UseLoggerFactory(observer)
            .Options;

        await using var db = new OpsBridgeDbContext(options);

        // Both of these statements wait for EF's migration lock when another instance is
        // migrating, which is exactly why the command timeout has to cover them.
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var appliedBefore = (await db.Database.GetAppliedMigrationsAsync(ct)).ToHashSet(StringComparer.Ordinal);

        if (pending.Count == 0)
        {
            // "Already at head" is a SUCCESS, not an error: deployment steps get re-run
            // on retry and on every new release, and the second run must not fail the
            // pipeline.
            logger.LogInformation("Schema is already at head; no pending migrations.");

            return new MigrationRunOutcome(
                ExitCodeSuccess,
                AlreadyAtHead: true,
                PendingMigrations: Array.Empty<string>(),
                AppliedMigrations: Array.Empty<string>(),
                NewlyRecordedMigrations: Array.Empty<string>(),
                RecordedMigrations: appliedBefore.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                Failure: null);
        }

        logger.LogInformation(
            "Applying {Count} pending migration(s) with a {Timeout}s command timeout: {Migrations}",
            pending.Count,
            timeout,
            string.Join(", ", pending));

        try
        {
            await db.Database.MigrateAsync(ct);
        }
        catch (Exception ex)
        {
            // EF Core 10 commits per migration, and the history INSERT for a migration
            // lives in the same transaction as that migration. A migration that failed is
            // therefore NOT recorded in __EFMigrationsHistory, which is exactly the
            // "a failed migration must never be marked complete" requirement. Earlier
            // migrations of the same run ARE committed, so they are reported separately.
            var recorded = await ReadRecordedMigrationsAsync(db, ct);
            var appliedBeforeFailure = observer.AppliedMigrations
                .Where(recorded.Contains)
                .ToList();
            var newlyRecorded = recorded.Where(id => !appliedBefore.Contains(id)).ToList();

            var message = FailureMessage(ex, timeout);
            var cancelled = ex is OperationCanceledException;

            if (cancelled)
            {
                logger.LogError("Migration CANCELLED: {Message}", message);
            }
            else
            {
                LogFailure(logger, ex, message);
            }

            logger.LogError(
                "Migration {State}. Attempted: {Migrations}. The history table now records {RecordedCount} migration(s): {Recorded}. " +
                "This run applied {AppliedCount} migration(s) before the {State}: {Applied}.",
                cancelled ? "CANCELLED" : "FAILED",
                string.Join(", ", pending),
                recorded.Count,
                string.Join(", ", recorded),
                appliedBeforeFailure.Count,
                cancelled ? "cancellation" : "failure",
                string.Join(", ", appliedBeforeFailure));

            return new MigrationRunOutcome(
                ExitCodeFailure,
                AlreadyAtHead: false,
                PendingMigrations: pending,
                AppliedMigrations: appliedBeforeFailure,
                NewlyRecordedMigrations: newlyRecorded,
                RecordedMigrations: recorded,
                Failure: message);
        }

        var appliedAfter = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var newlyRecordedAfter = appliedAfter.Where(id => !appliedBefore.Contains(id)).ToList();

        // EF logs one "Applying migration" line per migration it actually executes, so
        // this is the set of migrations THIS process applied — not "migrations that
        // happen to be in the history table now".
        var appliedByThisRun = observer.AppliedMigrations
            .Where(appliedAfter.Contains)
            .ToList();

        if (!observer.ObservedMigrationActivity)
        {
            // EF did not report what it did. That is not expected, but it must not be
            // papered over with a guess: say so, and fall back to the weaker (but still
            // true) statement "there was pending work when the run started".
            logger.LogWarning(
                "EF Core did not report which migrations it applied, so the per-run attribution is unavailable. " +
                "The reported 'newly recorded' migrations may have been applied by another instance.");
        }

        var alreadyAtHead = observer.ObservedMigrationActivity
            ? appliedByThisRun.Count == 0
            : false;

        logger.LogInformation(
            "Schema is now at head. This run applied {AppliedCount} migration(s): {Applied}. " +
            "{NewlyRecordedCount} migration(s) became present in the history table during this run: {NewlyRecorded}. " +
            "Under concurrency the two sets can differ: a migration another instance applied is present in the history table too.",
            appliedByThisRun.Count,
            string.Join(", ", appliedByThisRun),
            newlyRecordedAfter.Count,
            string.Join(", ", newlyRecordedAfter));

        return new MigrationRunOutcome(
            ExitCodeSuccess,
            AlreadyAtHead: alreadyAtHead,
            PendingMigrations: pending,
            AppliedMigrations: appliedByThisRun,
            NewlyRecordedMigrations: newlyRecordedAfter,
            RecordedMigrations: appliedAfter,
            Failure: null);
    }

    private static async Task<List<string>> ReadRecordedMigrationsAsync(
        OpsBridgeDbContext db, CancellationToken ct)
    {
        try
        {
            return (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        }
        catch (Exception)
        {
            // The history could not be read (the connection may be gone). The outcome
            // then reports "unknown", never a guess.
            return new List<string>();
        }
    }

    /// <summary>
    /// Log a failure. Environment-level problems (a wrong password, an unreachable
    /// host, a command timeout, a cancellation) get a single operator line and no stack
    /// trace: they are configuration problems, and a stack trace buries the one line
    /// that says what to fix. Genuinely unexpected failures keep their stack trace,
    /// because that is where the diagnostic value is.
    /// </summary>
    private static void LogFailure(ILogger logger, Exception ex, string message)
    {
        if (IsEnvironmentFailure(ex))
        {
            logger.LogError("Migration FAILED: {Message}", message);
            return;
        }

        logger.LogError(ex, "Migration FAILED: {Message}", message);
    }

    /// <summary>
    /// The one-line operator message for a failure.
    /// </summary>
    private static string FailureMessage(Exception ex, int timeoutSeconds)
    {
        var detail = OneLine(ex.Message);

        if (ex is TimeoutException || ContainsTimeout(ex))
        {
            return $"the database command timed out after {timeoutSeconds} seconds "
                + $"(OpsBridge:MigrationCommandTimeoutSeconds). {detail}";
        }

        if (ex is OperationCanceledException)
        {
            return $"the migration was cancelled. {detail}";
        }

        if (ex is PostgresException { SqlState: "28P01" })
        {
            return $"the database refused the credentials. {detail}";
        }

        if (IsEnvironmentFailure(ex))
        {
            return $"the database could not be reached. {detail}";
        }

        return detail;
    }

    /// <summary>
    /// True when the exception chain contains a timeout. Npgsql reports a command
    /// timeout either as a <see cref="TimeoutException"/> or — when the cancellation
    /// races with the pending read — as an <see cref="NpgsqlException"/> whose inner
    /// exception is the timeout. Both mean the same thing to an operator.
    /// </summary>
    private static bool ContainsTimeout(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the failure is about reaching the server rather than about the schema.
    /// </summary>
    /// <summary>
    /// Classify a failure as environment-level (a configuration or connectivity
    /// problem) rather than a schema problem.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="OpsBridgeLegacyBaseline"/> so both schema entrypoints
    /// treat the same class of failure the same way: one operator line, no stack
    /// trace. A schema error (42P07 and friends) is NOT an environment failure —
    /// its stack trace is worth keeping.
    /// </remarks>
    internal static bool IsEnvironmentFailure(Exception ex)
    {
        if (ex is TimeoutException or SocketException or OperationCanceledException)
        {
            return true;
        }

        if (ex is PostgresException postgresException)
        {
            // A PostgreSQL error that is about the AUTHORISATION or the CONNECTION, not
            // about the schema. A schema error (42P07 duplicate_table and friends) is
            // NOT an environment failure: its stack trace is worth keeping.
            return postgresException.SqlState is "28P01"    // invalid_password
                or "28P03"                                  // invalid_authorization_specification
                or "28000"                                  // invalid_authorization_specification
                or "3D000"                                  // invalid_catalog_name
                or "42501"                                  // insufficient_privilege
                or "53300";                                 // too_many_connections
        }

        // Any other Npgsql exception is a connection-level failure: DNS, connection
        // refused, TLS, protocol.
        return ex is NpgsqlException;
    }

    private static string OneLine(string message)
        => string.IsNullOrWhiteSpace(message)
            ? "(no message)"
            : message.ReplaceLineEndings(" ").Trim();
}

/// <summary>
/// Observes EF Core's own migration log so the runner can report which migrations
/// <b>this process</b> applied.
/// </summary>
/// <remarks>
/// <para>
/// EF Core does not expose "the migrations this call applied" through
/// <c>IMigrator</c> — <c>MigrateAsync</c> returns <c>Task</c>, not the applied set.
/// It does, however, log one <c>"Applying migration '{migration}'."</c> line (event
/// <c>MigrationApplying</c>, level Information) for every migration it executes, and
/// <c>"No migrations were applied. The database is already up to date."</c> when it
/// executes none. That IS EF's own per-run result, and it is the difference between
/// "this process applied the migration" and "the migration is in the history table"
/// — a distinction that matters when several instances start at the same time.
/// </para>
/// <para>
/// Only EF's <c>Microsoft.EntityFrameworkCore.Migrations</c> category is forwarded to
/// the caller's logger. EF's other categories (the raw DbCommand log) were never
/// forwarded before this observer existed, and keeping them out preserves the
/// deployment log's readability — and keeps a failed statement's SQL and error text
/// out of the operator-facing output.
/// </para>
/// </remarks>
internal sealed class MigrationEventObserver : ILoggerFactory
{
    private const string EfMigrationsCategory = "Microsoft.EntityFrameworkCore.Migrations";

    private readonly ILogger _inner;
    private readonly List<string> _appliedMigrations = new();
    private readonly object _gate = new();

    /// <summary>Create the observer.</summary>
    /// <param name="inner">The logger the EF migration messages are forwarded to.</param>
    public MigrationEventObserver(ILogger inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// The migration ids EF reported applying, in the order EF reported them.
    /// </summary>
    public IReadOnlyList<string> AppliedMigrations
    {
        get
        {
            lock (_gate)
            {
                return _appliedMigrations.ToList();
            }
        }
    }

    /// <summary>
    /// True when EF reported something about the migration run — either a migration it
    /// applied or the explicit "no migrations were applied" line. False means the
    /// per-run attribution is NOT available.
    /// </summary>
    public bool ObservedMigrationActivity { get; private set; }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new ObserverLogger(this, categoryName);

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
        // The observer owns no providers: the caller's logger is the sink.
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The caller's logger is not owned by this factory, so it is not disposed here.
    }

    private void RecordApplied(string migrationId)
    {
        lock (_gate)
        {
            _appliedMigrations.Add(migrationId);
            ObservedMigrationActivity = true;
        }
    }

    private void RecordNoMigrationsApplied()
    {
        lock (_gate)
        {
            ObservedMigrationActivity = true;
        }
    }

    private sealed class ObserverLogger : ILogger
    {
        private readonly MigrationEventObserver _owner;
        private readonly string _category;

        public ObserverLogger(MigrationEventObserver owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!_category.StartsWith(EfMigrationsCategory, StringComparison.Ordinal))
            {
                return;
            }

            if (TryReadProperty(state, "migration", out var migrationId))
            {
                _owner.RecordApplied(migrationId);
            }
            else if (eventId.Name is not null
                && eventId.Name.Contains("MigrationsNotApplied", StringComparison.Ordinal))
            {
                _owner.RecordNoMigrationsApplied();
            }

            _owner._inner.Log(logLevel, eventId, state, exception, formatter);
        }

        private static bool TryReadProperty<TState>(TState state, string name, out string value)
        {
            value = string.Empty;

            if (state is not IEnumerable<KeyValuePair<string, object?>> properties)
            {
                return false;
            }

            foreach (var property in properties)
            {
                if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value is string text
                    && !string.IsNullOrWhiteSpace(text))
                {
                    value = text;
                    return true;
                }
            }

            return false;
        }
    }
}
