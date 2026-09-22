using FlashSale.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlashSale.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Product configuration
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasIndex(p => p.SKU).IsUnique();
            entity.Property(p => p.SKU).HasMaxLength(50);
            entity.Property(p => p.Name).HasMaxLength(200);
            entity.Property(p => p.Category).HasMaxLength(100);
            entity.Property(p => p.Description).HasMaxLength(1000);
            entity.Property(p => p.OriginalPrice).HasColumnType("decimal(18,2)");
            entity.Property(p => p.FlashSalePrice).HasColumnType("decimal(18,2)");
        });

        // Idempotency (authoritative layer): the same IdempotencyKey can never
        // produce two orders, even with at-least-once queue redelivery.
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasIndex(o => o.IdempotencyKey).IsUnique();

            // ADR-013 §6: ownership is OPTIONAL, so the anonymous order path is
            // unchanged. Restrict — not Cascade/SetNull — because an order is a
            // financial record: deleting a user must not silently erase or
            // orphan it. Deactivate the account instead.
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ADR-013 §1: login identity is unique. The column holds the NORMALIZED
        // (trimmed, lower-cased) email, so this index is the authoritative
        // duplicate check — not a pre-flight SELECT, which would race.
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(254).IsRequired();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.Role).HasMaxLength(16).IsRequired();
        });
    }
}

/// <summary>
/// Design-time factory so `dotnet ef migrations` works without booting the API.
/// Uses the same local-dev defaults as the composition roots.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres;Maximum Pool Size=80";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppDbContext(options);
    }
}

