using FlashSale.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// Adapter: checks PostgreSQL connectivity through EF Core.
/// Registered as Scoped because <see cref="AppDbContext"/> is Scoped.
/// </summary>
public sealed class DatabaseHealthCheck(AppDbContext db) : IDatabaseHealthCheck
{
    public async Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct);
        }
        catch
        {
            return false;
        }
    }
}
