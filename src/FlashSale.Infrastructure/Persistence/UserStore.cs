using FlashSale.Application.Auth;
using FlashSale.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of the auth persistence port (ADR-013).
/// </summary>
/// <remarks>
/// The load-bearing method here is <see cref="TryRotateTokenVersionAsync"/>: it
/// is a single conditional UPDATE, which is what makes refresh-token replay
/// impossible under concurrency. A read-validate-write sequence would let two
/// concurrent refreshes both observe the same version and both succeed.
/// </remarks>
public sealed class UserStore(AppDbContext db) : IUserStore
{
    /// <summary>PostgreSQL SQLSTATE for unique_violation.</summary>
    private const string UniqueViolation = "23505";

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<bool> TryAddAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // The unique index on Email is the authoritative duplicate check.
            // A pre-flight SELECT would race: two concurrent registrations could
            // both see "no such email" and both insert. Letting the database
            // arbitrate is the only correct version.
            db.Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryRotateTokenVersionAsync(Guid userId, int presentedVersion, CancellationToken ct = default)
    {
        // ONE statement, no read-then-write. The WHERE clause carries the
        // compare-and-swap: rows affected is 1 for the winner and 0 for a
        // replayed/stale token, so exactly one of two concurrent refreshes wins.
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Users"
            SET    "TokenVersion" = "TokenVersion" + 1
            WHERE  "Id" = {userId}
              AND  "TokenVersion" = {presentedVersion}
            """, ct);

        return affected == 1;
    }

    public async Task<int> BumpTokenVersionAsync(Guid userId, CancellationToken ct = default)
    {
        // Unconditional bump: logout must invalidate the refresh token even if
        // the caller's copy is already stale.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Users"
            SET    "TokenVersion" = "TokenVersion" + 1
            WHERE  "Id" = {userId}
            """, ct);

        var current = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (int?)u.TokenVersion)
            .FirstOrDefaultAsync(ct);

        return current ?? 0;
    }
}