using FlashSale.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Idempotency (authoritative layer): the same IdempotencyKey can never
        // produce two orders, even with at-least-once queue redelivery.
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasIndex(o => o.IdempotencyKey).IsUnique();
        });
    }
}
