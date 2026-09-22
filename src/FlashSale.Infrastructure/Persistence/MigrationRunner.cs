using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// Schema migration as an explicit, single-shot operation (Phase 7C).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DatabaseInitializer.InitializeAsync"/> applies migrations inside every
/// API and worker process. That is correct for docker-compose and wrong for Kubernetes:
/// the prod overlay runs <c>order-api</c> with <c>replicas: 2</c>, so two pods race to
/// <c>MigrateAsync</c> against the same PostgreSQL during a rollout. EF has advisory
/// locking, but the failure mode that actually bites is the one without a lock —
/// schema-changing migrations are not idempotent under concurrency, and a pod that
/// crashes mid-migration restarts into a half-migrated database.
/// </para>
/// <para>
/// The contract instead is: one Job runs migrations to completion, and only then do the
/// API and worker start. The workloads keep calling <c>InitializeAsync</c> so a
/// single-process local run still works, but the Job makes the ordered path the
/// deployment path.
/// </para>
/// <para>
/// Seeding is deliberately NOT part of this: seed data is a demo concern, and a Job that
/// mutates business rows would make "the migration succeeded" ambiguous. The Job's only
/// promise is <em>the schema is at head</em>.
/// </para>
/// </remarks>
public static class MigrationRunner
{
    /// <summary>
    /// Apply every pending EF migration. Returns non-zero exit codes so a Kubernetes
    /// Job can fail fast and loudly instead of hanging.
    /// </summary>
    /// <param name="services">A provider containing <see cref="AppDbContext"/>.</param>
    /// <param name="logger">Logger for progress lines that land in `kubectl logs`.</param>
    /// <param name="ct">Cancellation, so a Job deletion deadline interrupts cleanly.</param>
    /// <returns>0 on success, 1 on failure.</returns>
    public static async Task<int> RunAsync(
        IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Difference() is the honest report: it lists what is about to be applied,
        // and an empty list means "already at head" — which must be a SUCCESS, not
        // an error, because Jobs get re-run on retry and on every new release.
        var pending = await db.Database.GetPendingMigrationsAsync(ct);
        var pendingList = pending.ToList();

        if (pendingList.Count == 0)
        {
            logger.LogInformation("Schema already at head; no pending migrations.");
            return 0;
        }

        logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}",
            pendingList.Count, string.Join(", ", pendingList));

        try
        {
            await db.Database.MigrateAsync(ct);
        }
        catch (Exception ex)
        {
            // Logged and converted to an exit code rather than rethrown: the caller is a
            // Job entrypoint, and an unhandled exception here would print a stack trace
            // without ever naming the migrations that were attempted.
            logger.LogError(ex, "Migration FAILED. Attempted: {Migrations}",
                string.Join(", ", pendingList));
            return 1;
        }

        logger.LogInformation("Schema is now at head.");
        return 0;
    }
}
