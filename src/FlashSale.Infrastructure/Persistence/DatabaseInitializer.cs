using FlashSale.Application.Messaging;
using FlashSale.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// Startup concerns (ADR-006): schema comes from EF migrations (MigrateAsync),
/// demo seed is idempotent, and the Redis mirror is rebuilt from Postgres truth.
/// API and Worker both call this; concurrent MigrateAsync calls are safe because
/// the migrations history table insert is serialized by Postgres.
/// </summary>
public static class DatabaseInitializer
{
    private const int DemoStock = 100;

    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(ct);

        if (!await db.Products.AnyAsync(ct))
        {
            db.Products.Add(new Product { Name = "iPhone 15 Pro Max", AvailableStock = DemoStock });
            await db.SaveChangesAsync(ct);
        }

        var gateway = scope.ServiceProvider.GetRequiredService<IStockReservationGateway>();
        foreach (var product in await db.Products.AsNoTracking().ToListAsync(ct))
        {
            await gateway.SetStockAsync(product.Id, product.AvailableStock);
        }
    }

    /// <summary>Test/ops helper: apply schema only, without demo seeding.</summary>
    public static async Task MigrateOnlyAsync(AppDbContext db, CancellationToken ct = default) =>
        await db.Database.MigrateAsync(ct);
}

