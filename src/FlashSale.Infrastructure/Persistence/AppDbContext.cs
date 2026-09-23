using FlashSale.Domain;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Inventory;
using FlashSale.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlashSale.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<AutomationRun> AutomationRuns { get; set; } = null!;
    public DbSet<StockAlert> StockAlerts { get; set; } = null!;
    public DbSet<DailyReport> DailyReports { get; set; } = null!;

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
            // Spec §6: existing rows get the platform default reorder threshold.
            entity.Property(p => p.ReorderThreshold).HasDefaultValue(5);
        });

        // Order configuration (with automation lifecycle status, spec §4/§11)
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasIndex(o => o.IdempotencyKey).IsUnique();

            entity.Property(o => o.Status).HasConversion<string>();

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // User configuration (ADR-013 §1: login identity is unique)
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(254).IsRequired();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.Role).HasMaxLength(16).IsRequired();
        });

        // Automation run audit (spec §11)
        modelBuilder.Entity<AutomationRun>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.WorkflowName).HasMaxLength(100);
            entity.Property(r => r.TriggerType).HasMaxLength(50);
            entity.Property(r => r.TriggerId).HasMaxLength(100);
            entity.Property(r => r.Status).HasConversion<string>();
            entity.Property(r => r.ErrorCode).HasMaxLength(50);
            entity.Property(r => r.ErrorMessage).HasMaxLength(1000);
            entity.Property(r => r.ResultSummary).HasMaxLength(2000);

            entity.HasIndex(r => r.CorrelationId);
            entity.HasIndex(r => r.WorkflowName);
            entity.HasIndex(r => r.Status);
            entity.HasIndex(r => r.StartedAt);
        });

        // Low-stock alerts (spec §6). The partial unique index is the dedupe
        // backstop: at most ONE Open alert per product, so a rescan (or a race
        // between two workers) cannot spam duplicates.
        modelBuilder.Entity<StockAlert>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(a => a.ProductId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'");
            entity.HasIndex(a => a.CreatedAt);
        });

        // Daily business reports (spec §7): one row per UTC day — the unique
        // index is what makes a rerun an upsert instead of a duplicate.
        modelBuilder.Entity<DailyReport>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.ReportDate).IsUnique();
            entity.Property(r => r.Revenue).HasColumnType("decimal(18,2)");
            entity.Property(r => r.AverageOrderValue).HasColumnType("decimal(18,2)");
        });
    }
}

/// <summary>Design-time factory so `dotnet ef migrations` works without booting the API.</summary>
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

