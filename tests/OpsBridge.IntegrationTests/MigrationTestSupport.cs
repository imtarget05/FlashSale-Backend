using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpsBridge.Domain.Connectors;
using OpsBridge.Domain.Ingestion;
using OpsBridge.Domain.Reconciliation;
using OpsBridge.Infrastructure.Persistence;

namespace OpsBridge.IntegrationTests;

/// <summary>
/// Hand-written <see cref="ILogger"/> that keeps every formatted message so a test can
/// assert on the DIAGNOSTIC TEXT the production code emits, not only on the structured
/// outcome. The repository has no mocking library, so this is the fake.
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly List<string> _messages = new();
    private readonly object _gate = new();

    /// <summary>Every message logged so far, in order.</summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToList();
            }
        }
    }

    /// <summary>All messages joined with newlines — convenient for "contains" assertions.</summary>
    public string AllText => string.Join("\n", Messages);

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
        var message = formatter(state, exception);

        lock (_gate)
        {
            _messages.Add(message);

            if (exception is not null)
            {
                _messages.Add(exception.ToString());
            }
        }
    }
}

/// <summary>
/// A uniquely named throw-away PostgreSQL database on the fixture's server.
/// </summary>
/// <remarks>
/// The fixture owns exactly one database (<c>OpsBridgeTestDb</c>) that the other
/// integration tests share. Every migration-lifecycle test needs its OWN database,
/// because the whole point of the matrix is "this database starts in state X". The
/// shared database is never truncated and never dropped by these tests.
/// </remarks>
internal sealed class MigrationTestDatabase : IAsyncDisposable
{
    private readonly NpgsqlConnectionStringBuilder _adminBuilder;

    private MigrationTestDatabase(NpgsqlConnectionStringBuilder adminBuilder, string databaseName)
    {
        _adminBuilder = adminBuilder;
        DatabaseName = databaseName;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            Database = databaseName
        }.ConnectionString;
    }

    /// <summary>Name of the throw-away database.</summary>
    public string DatabaseName { get; }

    /// <summary>Connection string pointing at the throw-away database.</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Create a uniquely named database on the server behind <paramref name="adminConnectionString"/>.
    /// </summary>
    /// <param name="adminConnectionString">The fixture's connection string.</param>
    /// <param name="prefix">Short human-readable prefix, e.g. <c>mig_fresh</c>.</param>
    public static async Task<MigrationTestDatabase> CreateAsync(
        string adminConnectionString, string prefix)
    {
        var admin = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = "postgres"
        };

        var databaseName = $"{prefix}_{Guid.NewGuid():N}";

        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();

        return new MigrationTestDatabase(admin, databaseName);
    }

    /// <summary>
    /// Drop the throw-away database. Never fails the test: a leftover scratch database
    /// is a cleanup problem, not a behavioural one, and a failing cleanup would mask the
    /// real assertion.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_adminBuilder.ConnectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
            // Cleanup only.
        }
    }
}

/// <summary>
/// Small read-only query helpers for asserting on database state directly, instead of
/// through the EF context — the point of several scenarios is what the DATABASE looks
/// like, not what EF thinks it looks like.
/// </summary>
internal static class Db
{
    /// <summary>Name of EF Core's migration history table.</summary>
    public const string HistoryTableName = "__EFMigrationsHistory";

    /// <summary>Names of all user tables in the <c>public</c> schema.</summary>
    public static async Task<List<string>> TableNamesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
            ORDER BY c.relname
            """,
            connection);

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    /// <summary>True when the table exists in the <c>public</c> schema.</summary>
    public static async Task<bool> TableExistsAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relname = @tableName
                  AND c.relkind IN ('r', 'p'))
            """,
            connection);

        command.Parameters.AddWithValue("tableName", tableName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    /// <summary>
    /// Number of rows in the migration history table. Zero when the table does not
    /// exist — a failed migration must not leave a history row behind, and the table
    /// itself may legitimately never have been created.
    /// </summary>
    public static async Task<long> HistoryRowCountAsync(string connectionString)
    {
        if (!await TableExistsAsync(connectionString, HistoryTableName))
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
             SELECT COUNT(*) FROM "{HistoryTableName}"
             """,
            connection);

        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value ?? 0L);
    }

    /// <summary>Number of rows in a table.</summary>
    public static async Task<long> CountRowsAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM \"{tableName}\"", connection);

        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value ?? 0L);
    }
}

/// <summary>
/// The shared helpers of the migration tests: the process-level entrypoint, the legacy
/// database shape, and the SQL escape hatch.
/// </summary>
/// <remarks>
/// These used to be copy-pasted into both migration test classes. They live here
/// because they describe ONE thing — "a database in state X" — and a divergence
/// between the two copies would be a silent difference in what the tests actually
/// prove.
/// </remarks>
internal static class MigrationTestHelpers
{
    /// <summary>Environment variable that carries <c>ConnectionStrings:OpsBridgeDb</c>.</summary>
    public const string ConnectionStringEnvironmentVariable = "ConnectionStrings__OpsBridgeDb";

    /// <summary>Environment variable that carries <c>ConnectionStrings:DefaultConnection</c>.</summary>
    public const string DefaultConnectionEnvironmentVariable = "ConnectionStrings__DefaultConnection";

    /// <summary>
    /// The environment a deployment step runs with: a connection string, and no JWT
    /// signing key at all. The JWT key is explicitly removed so the test proves the
    /// migration step needs no application identity.
    /// </summary>
    public static Dictionary<string, string?> MigrationEnvironment(string connectionString)
        => new()
        {
            [ConnectionStringEnvironmentVariable] = connectionString,
            [DefaultConnectionEnvironmentVariable] = null,
            ["Jwt__SigningKey"] = null,
            ["Jwt__Issuer"] = null,
            ["Jwt__Audience"] = null,
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DOTNET_ENVIRONMENT"] = "Production"
        };

    /// <summary>
    /// Locate the built API assembly the process-level tests execute. Fails the test
    /// when it is missing — a skipped test would hide a broken build.
    /// </summary>
    public static string RequireApiDll()
    {
        var located = OpsBridgeApiLocator.TryLocate(out var searched);
        Assert.True(
            located is not null,
            $"Could not locate 'src/OpsBridge.Api/bin/<config>/net10.0/OpsBridge.Api.dll'. " +
            $"Build the solution before running the integration tests " +
            $"(dotnet build OpsBridge.slnx). Searched: {string.Join(", ", searched)}");

        return located!;
    }

    /// <summary>
    /// Recreate the LEGACY code path exactly: a fresh <see cref="OpsBridgeDbContext"/>
    /// whose schema was created by <c>EnsureCreatedAsync()</c>, i.e. no migration
    /// history table at all.
    /// </summary>
    public static async Task CreateLegacyDatabaseAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OpsBridgeDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new OpsBridgeDbContext(options);
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>Seed one row into each of the three user tables of a legacy database.</summary>
    public static async Task SeedLegacyRowsAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OpsBridgeDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new OpsBridgeDbContext(options);

        db.Connectors.Add(new ConnectorConfig
        {
            Id = Guid.NewGuid(),
            Name = "Legacy Connector",
            Type = ConnectorType.FactoryFlowWebhook,
            Secret = "legacy-secret",
            BaseUrl = "https://legacy.example/api",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });

        db.IngestedEvents.Add(new IngestedEvent
        {
            EventId = Guid.NewGuid(),
            EventType = "goods.receipt",
            SchemaVersion = "v1",
            Source = "legacy-source",
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid(),
            RawPayload = "{\"legacy\":true}",
            Status = EventProcessingStatus.Received,
            ReceivedAt = DateTimeOffset.UtcNow
        });

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            EntityType = "ConnectorConfig",
            EntityId = Guid.NewGuid().ToString(),
            Action = "Created",
            UserId = "legacy-user",
            Timestamp = DateTimeOffset.UtcNow,
            Details = "{\"legacy\":true}"
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Run one statement against a database.</summary>
    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Create a LOGIN role WITHOUT the <c>CREATEDB</c> privilege and return a
    /// connection string that uses it.
    /// </summary>
    /// <remarks>
    /// This is how the "CREATE DATABASE refused" fail-closed guarantee is proved for
    /// real: the role genuinely cannot create a database, so the tool has to refuse
    /// rather than fall back to an unverified baseline.
    /// </remarks>
    public static async Task<string> CreateRoleWithoutCreateDbAsync(
        string adminConnectionString, string databaseName, string roleName, string password)
    {
        await ExecuteAsync(
            adminConnectionString,
            $"CREATE ROLE {QuoteIdentifier(roleName)} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB");

        await ExecuteAsync(
            adminConnectionString,
            $"GRANT CONNECT ON DATABASE {QuoteIdentifier(databaseName)} TO {QuoteIdentifier(roleName)}");

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = roleName,
            Password = password,
            Database = databaseName
        };

        return builder.ConnectionString;
    }

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

/// <summary>Result of running the API assembly as a child process.</summary>
/// <param name="ExitCode">Process exit code, or -1 when the process was killed on timeout.</param>
/// <param name="StandardOutput">Everything the process wrote to stdout.</param>
/// <param name="StandardError">Everything the process wrote to stderr.</param>
/// <param name="TimedOut">True when the process had to be killed because it exceeded the timeout.</param>
internal sealed record ApiProcessResult(
    int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    /// <summary>stdout and stderr together — the deployment log a human would read.</summary>
    public string CombinedOutput => string.Concat(StandardOutput, "\n", StandardError);
}

/// <summary>
/// Locates the built <c>OpsBridge.Api.dll</c> that the process-level entrypoint tests
/// execute.
/// </summary>
/// <remarks>
/// CI builds <c>OpsBridge.slnx</c> before running the integration tests, so
/// <c>src/OpsBridge.Api/bin/&lt;config&gt;/net10.0/OpsBridge.Api.dll</c> exists. The
/// configuration folder is detected from the running test assembly's own path, which
/// always sits under <c>tests/OpsBridge.IntegrationTests/bin/&lt;config&gt;/&lt;tfm&gt;/</c>.
/// </remarks>
internal static class OpsBridgeApiLocator
{
    /// <summary>
    /// Try to locate the API assembly. Returns <c>null</c> (and the searched paths) when
    /// it cannot be found — the caller must fail the test, never skip it.
    /// </summary>
    public static string? TryLocate(out IReadOnlyList<string> searchedPaths)
    {
        var searched = new List<string>();
        var preferredConfigurations = new List<string>();

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? repositoryRoot = null;

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpsBridge.slnx")))
            {
                repositoryRoot = directory.FullName;
                break;
            }

            if (string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase)
                && directory.Parent is not null
                && !preferredConfigurations.Contains(directory.Parent.Name))
            {
                preferredConfigurations.Add(directory.Parent.Name);
            }

            directory = directory.Parent;
        }

        if (repositoryRoot is null)
        {
            searched.Add($"(repository root not found from {AppContext.BaseDirectory})");
            searchedPaths = searched;
            return null;
        }

        // The configuration of the running test assembly first, then the other
        // configurations, so a Debug test run finds the Debug build and vice versa.
        foreach (var configuration in preferredConfigurations.Concat(new[] { "Release", "Debug" }))
        {
            var configurationRoot = Path.Combine(repositoryRoot, "src", "OpsBridge.Api", "bin", configuration);
            if (!Directory.Exists(configurationRoot))
            {
                continue;
            }

            foreach (var frameworkDirectory in Directory.EnumerateDirectories(configurationRoot))
            {
                var candidate = Path.Combine(frameworkDirectory, "OpsBridge.Api.dll");
                searched.Add(candidate);

                if (File.Exists(candidate))
                {
                    searchedPaths = searched;
                    return candidate;
                }
            }
        }

        searchedPaths = searched;
        return null;
    }
}

/// <summary>
/// Runs the built API assembly as a child process so the deployment entrypoints
/// (<c>--migrate</c>, <c>--baseline-legacy</c>) can be exercised exactly the way a
/// deployment pipeline exercises them: by exit code and by log output.
/// </summary>
internal static class ApiProcessRunner
{
    /// <summary>Hard timeout for a child process.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Run <c>dotnet &lt;apiDll&gt; &lt;args&gt;</c> with the given environment.
    /// </summary>
    /// <param name="apiDll">Full path to <c>OpsBridge.Api.dll</c>.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="environment">Environment variables to set (or remove, with a null value) for the child.</param>
    /// <param name="timeout">Timeout; the child is killed when it is exceeded.</param>
    public static async Task<ApiProcessResult> RunAsync(
        string apiDll,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? Timeout;

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDotnetPath(),
            WorkingDirectory = Path.GetDirectoryName(apiDll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(apiDll);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            if (pair.Value is null)
            {
                startInfo.Environment.Remove(pair.Key);
            }
            else
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardOutput.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardError.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start the child process for {apiDll}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cancellation = new CancellationTokenSource(effectiveTimeout);

        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // A hung deployment step must not wedge CI: kill the whole tree and report
            // the timeout instead of blocking forever.
            TryKill(process);

            return new ApiProcessResult(-1, standardOutput.ToString(), standardError.ToString(), TimedOut: true);
        }

        return new ApiProcessResult(
            process.ExitCode, standardOutput.ToString(), standardError.ToString(), TimedOut: false);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill.
        }
    }

    private static string ResolveDotnetPath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath)
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            var candidate = Path.Combine(
                dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }
}
