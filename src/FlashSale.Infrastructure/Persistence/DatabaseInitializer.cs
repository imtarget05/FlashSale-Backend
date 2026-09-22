using FlashSale.Application.Inventory;
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


    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(ct);

        if (!await db.Products.AnyAsync(ct))
        {
            var seedProducts = new List<Product>
            {
                // Electronics
                new Product { SKU = "APL-MBP14-M3M", Name = "Apple MacBook Pro 14 M3 Max 36GB/1TB", Category = "Electronics", Description = "Laptop Apple MacBook Pro 14 inch M3 Max, 36GB RAM, 1TB SSD", OriginalPrice = 80000000, FlashSalePrice = 69990000, AvailableStock = 3 },
                new Product { SKU = "SNY-WH1000XM5", Name = "Tai nghe Bluetooth Sony WH-1000XM5", Category = "Electronics", Description = "Tai nghe chống ồn chủ động không dây Sony WH-1000XM5", OriginalPrice = 7990000, FlashSalePrice = 5490000, AvailableStock = 50 },
                new Product { SKU = "LG-27GR95QE", Name = "Màn hình LG UltraGear 27GR95QE-B", Category = "Electronics", Description = "Màn hình OLED LG UltraGear 27 inch 240Hz 0.03ms", OriginalPrice = 24000000, FlashSalePrice = 18990000, AvailableStock = 10 },
                new Product { SKU = "APL-IP15PM-256", Name = "iPhone 15 Pro Max 256GB", Category = "Electronics", Description = "Điện thoại Apple iPhone 15 Pro Max 256GB", OriginalPrice = 34990000, FlashSalePrice = 29500000, AvailableStock = 5 },
                new Product { SKU = "SS-S24U-512", Name = "Samsung Galaxy S24 Ultra 512GB", Category = "Electronics", Description = "Điện thoại Samsung Galaxy S24 Ultra 512GB", OriginalPrice = 37990000, FlashSalePrice = 31990000, AvailableStock = 15 },
                
                // Home Appliances
                new Product { SKU = "RBR-S8PRO", Name = "Robot hút bụi lau nhà Roborock S8 Pro Ultra", Category = "Home Appliances", Description = "Robot hút bụi lau nhà tự động giặt giẻ, sấy khô Roborock S8 Pro Ultra", OriginalPrice = 29990000, FlashSalePrice = 21990000, AvailableStock = 8 },
                new Product { SKU = "PHL-HD9252", Name = "Nồi chiên không dầu Philips HD9252/90", Category = "Home Appliances", Description = "Nồi chiên không dầu điện tử Philips HD9252/90 4.1L", OriginalPrice = 2490000, FlashSalePrice = 1290000, AvailableStock = 100 },
                new Product { SKU = "DSN-V12D", Name = "Máy hút bụi Dyson V12 Detect Slim Absolute", Category = "Home Appliances", Description = "Máy hút bụi không dây Dyson V12", OriginalPrice = 18990000, FlashSalePrice = 14990000, AvailableStock = 20 },

                // Fashion & Beauty
                new Product { SKU = "EL-ANR-50", Name = "Serum phục hồi Estee Lauder Advanced Night Repair 50ml", Category = "Beauty", Description = "Tinh chất phục hồi chống lão hóa Estee Lauder Advanced Night Repair Synchronized Multi-Recovery Complex 50ml", OriginalPrice = 3700000, FlashSalePrice = 2590000, AvailableStock = 200 },
                new Product { SKU = "LRP-EFF-40", Name = "Kem giảm mụn La Roche-Posay Effaclar Duo+ 40ml", Category = "Beauty", Description = "Kem dưỡng giảm mụn, ngăn ngừa vết thâm La Roche-Posay Effaclar Duo+ 40ml", OriginalPrice = 525000, FlashSalePrice = 350000, AvailableStock = 500 },
                new Product { SKU = "NKE-AF1-WHT", Name = "Giày Thể Thao Nike Air Force 1 '07 White", Category = "Fashion", Description = "Giày thể thao nam nữ Nike Air Force 1 '07 màu trắng All White", OriginalPrice = 2920000, FlashSalePrice = 2100000, AvailableStock = 30 }
            };

            db.Products.AddRange(seedProducts);
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

