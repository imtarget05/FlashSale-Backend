using FlashSale.Application.Messaging;
using FlashSale.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// Startup concerns kept out of the composition root: create schema, seed the
/// demo product, and mirror stock into the reservation tier (ADR-003).
/// </summary>
public static class DatabaseInitializer
{
    private const int DemoStock = 100;

    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.EnsureCreatedAsync(ct);

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
}
